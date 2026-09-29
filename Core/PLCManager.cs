using System.Buffers.Binary;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using TrackedVehicle.Model;

namespace TrackedVehicle.Core;

/// <summary>单例 PLC TCP 客户端，接收 8 字节状态报文，发送 3 字节识别结果；整数均高字节在前。</summary>
public sealed class PLCManager(IOptions<VehicleOptions> options, ILogger<PLCManager> logger)
{
    private readonly object _connectionLock = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private NetworkStream? _stream;
    private int _running;
    // 首次自动模式只触发一次启动；异步续接避免在 TCP 接收线程中执行开相机、存库等操作。
    // 巡检结束尚未接入，因此重连、重复报文及再次进入模式 3 均不新建巡检。
    private readonly TaskCompletionSource _automaticModeReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task WaitForAutomaticModeAsync(CancellationToken token) => _automaticModeReceived.Task.WaitAsync(token);

    /// <summary>当前是否持有连接；远端意外断网可能要等下一次读写失败才被发现。</summary>
    public bool IsConnected
    {
        get { lock (_connectionLock) { return _stream is not null; } }
    }

    /// <summary>连接、接收、断线重连循环，取消后等待连接资源释放再返回。</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            throw new InvalidOperationException("PLC 通信循环已启动，不能重复启动。");
        }

        using var simulationLifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Task simulationTask = Task.CompletedTask;
        try
        {
            var settings = options.Value.PLC;
            if (!settings.Enabled)
            {
                logger.LogInformation("PLC 通信已在配置中禁用");
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
                return;
            }

            if (settings.Simulation.Enabled)
            {
                var data = settings.Simulation.ParseReceiveData();
                logger.LogWarning("PLC 模拟接收已启用：每 {IntervalMs} 毫秒注入一次报文；真实 TCP 连接和收发保持运行，中心点仍发送给真实 PLC",
                    settings.Simulation.IntervalMilliseconds);
                // 模拟接收独立于连接和重连，首次立即注入；由本方法持有任务，退出时取消并等待结束。
                simulationTask = SimulateReceiveAsync(data, settings.Simulation.IntervalMilliseconds, simulationLifetime.Token);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                // 识别结果只有 3 字节，禁用 Nagle 合并等待，让每次结果尽快写入网络。
                using (var client = new TcpClient { NoDelay = true })
                {
                    try
                    {
                        logger.LogInformation("正在连接 PLC：{IP}:{Port}", settings.IP, settings.Port);
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                        timeout.CancelAfter(TimeSpan.FromSeconds(settings.ConnectTimeoutSeconds));
                        await client.ConnectAsync(settings.IP, settings.Port, timeout.Token);
                        using var stream = client.GetStream();
                        lock (_connectionLock) { _stream = stream; }
                        logger.LogInformation("PLC TCP 连接已建立：{IP}:{Port}", settings.IP, settings.Port);
                        await ReceiveDataAsync(stream, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        logger.LogWarning("PLC 连接超时");
                    }
                    catch (Exception exception) when (exception is SocketException or IOException or ObjectDisposedException)
                    {
                        logger.LogWarning(exception, "PLC TCP 通信中断");
                    }
                    finally
                    {
                        lock (_connectionLock) { _stream = null; }
                    }
                }

                logger.LogInformation("将在 {Seconds} 秒后重连 PLC", settings.ReconnectIntervalSeconds);
                await Task.Delay(TimeSpan.FromSeconds(settings.ReconnectIntervalSeconds), stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 正常停止，包括正在等待重连或 PLC 被禁用的情况。
        }
        finally
        {
            simulationLifetime.Cancel();
            await simulationTask;
            Interlocked.Exchange(ref _running, 0);
            logger.LogInformation("PLC 通信已停止，连接已关闭");
        }
    }

    /// <summary>按原样发送字节，并发调用串行写入；失败不自动重发，避免重复执行 PLC 指令。</summary>
    public async Task SendDataAsync(byte[] command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Length == 0) { throw new ArgumentException("发送数据不能为空。", nameof(command)); }
        cancellationToken.ThrowIfCancellationRequested();

        // 绑定调用时的连接，排队期间断线则报错，不把旧命令发送到重连后的新连接。
        NetworkStream stream;
        lock (_connectionLock) { stream = _stream ?? throw new InvalidOperationException("PLC 尚未连接。"); }
        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            lock (_connectionLock)
            {
                if (!ReferenceEquals(stream, _stream)) { throw new InvalidOperationException("PLC 连接已变化，请重新确认指令。"); }
            }
            await stream.WriteAsync(command, cancellationToken);
            logger.LogDebug("已向 PLC 写入 {ByteCount} 字节", command.Length);
        }
        catch (Exception exception) when (exception is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // 写入可能只完成一部分；关闭当前连接，使接收循环退出并重连。
            lock (_connectionLock)
            {
                if (ReferenceEquals(stream, _stream)) { _stream = null; }
            }
            stream.Dispose();
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>成功识别后发送中心点原值，就绪位为 1，巡检结束位暂为 0；不缓存或重发过期结果。</summary>
    public async Task SendDetectionResultAsync(int centerX, CancellationToken token)
    {
        // 协议只有两个字节，超出可表示范围就报错，不能截断、缩放或把负数包装成位置。
        if (centerX < 0 || centerX > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(centerX), centerX, "中心点无法用两个无符号字节表示。");
        var data = new byte[3];
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0, 2), (ushort)centerX);
        data[2] = 0x01;
        // 正常发送不加延时；网络写入最多等待 1 秒，超时由发送接口断开连接，避免无限阻塞识别。
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        await SendDataAsync(data, timeout.Token);
    }

    private async Task ReceiveDataAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[8];
        int received = 0;
        while (true)
        {
            // TCP 不保留报文边界：只读取当前报文缺少的字节，凑齐 8 字节才解析。
            // 缓冲区属于本次连接，断线后丢弃残包，不能与重连后的数据拼接。
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(received), cancellationToken);
            if (bytesRead == 0)
            {
                logger.LogWarning("PLC 已关闭 TCP 连接，丢弃未完成报文 {ByteCount} 字节", received);
                return;
            }

            received += bytesRead;
            if (received < buffer.Length) continue;
            received = 0;
            ProcessReceivedData(buffer, false);
        }
    }

    private async Task SimulateReceiveAsync(byte[] data, int intervalMilliseconds, CancellationToken token)
    {
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                ProcessReceivedData(data, true);
                // 单位为毫秒；只注入接收数据，不占用真实收包循环，停止时取消等待。
                await Task.Delay(intervalMilliseconds, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void ProcessReceivedData(byte[] data, bool simulated)
    {
        // 真实接收已凑齐 8 字节，模拟接收也先校验长度，两种来源共用业务解析和启动条件。
        var mode = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(0, 2));
        var speed = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(6, 2));
        logger.LogInformation("PLC {ReceiveType} 8 字节：{Hex}，模式 {Mode}，车速原值 {Speed}",
            simulated ? "模拟接收" : "真实接收", BitConverter.ToString(data), mode, speed);
        if (mode == 3) _automaticModeReceived.TrySetResult();
    }
}
