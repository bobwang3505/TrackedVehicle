using System.Net.Sockets;
using Microsoft.Extensions.Options;
using TrackedVehicle.Model;

namespace TrackedVehicle.Core;

/// <summary>单例 PLC TCP 客户端，由控制中心负责启动和取消；暂不解析业务协议。</summary>
public sealed class PLCManager(IOptions<VehicleOptions> options, ILogger<PLCManager> logger)
{
    private readonly object _connectionLock = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private NetworkStream? _stream;
    private int _running;

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

        try
        {
            var settings = options.Value.PLC;
            if (!settings.Enabled)
            {
                logger.LogInformation("PLC 通信已在配置中禁用");
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                using (var client = new TcpClient())
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
            Interlocked.Exchange(ref _running, 0);
            logger.LogInformation("PLC 通信已停止，连接已关闭");
        }
    }

    /// <summary>按原样发送字节，并发调用串行写入；失败不自动重发，避免重复执行 PLC 指令。</summary>
    public async Task SendDataAsync(byte[] command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Length == 0) { throw new ArgumentException("发送数据不能为空。", nameof(command)); }

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

    private async Task ReceiveDataAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var buffer = new byte[1024];
        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                logger.LogWarning("PLC 已关闭 TCP 连接");
                return;
            }

            logger.LogInformation("收到 PLC TCP 数据片段，共 {ByteCount} 字节：{Hex}",
                bytesRead, BitConverter.ToString(buffer, 0, bytesRead));
            // TODO：按真实 PLC 协议缓存并拆包。一次 Read 不一定是一条完整报文，不能直接逐次解码文本。
        }
    }
}
