using System.Collections.Concurrent;
using System.Diagnostics;
using TrackedVehicle.Model;
using TrackedVehicle.NativeInterop;

namespace TrackedVehicle.Core;

/// <summary>按相机编号保存最新通知，后台逐台限频识别。</summary>
public sealed class DetectionScheduler(IDetectManager detector, ILogger<DetectionScheduler> logger)
{
    // key 为 C++ SDK 返回的 camId，value 为该相机的识别配置和运行状态。
    private readonly ConcurrentDictionary<int, CameraState> cameraDitc = new();
    // 信号量：初始名额为 1，最大名额为 1，因此同一时间只允许一个操作进入。
    // 识别流程中需要 await，不能使用普通 lock，改用 WaitAsync 异步等待名额。
    // 识别前取得名额，结束后在 finally 中 Release 归还；成功取得后必须释放。
    // 注销相机时用 Wait 同步等待名额，确保正在执行的识别结束后，调用方才能关相机。
    // 相机帧回调只更新最新通知，不获取此信号量，因此不会等待识别完成。
    private readonly SemaphoreSlim _execution = new(1, 1);
    private int _running;

    private sealed class FrameNotice
    {
        // 创建时赋值 之后不能修改 只读
        public long Index { get; }

        // 创建时赋值
        public FrameNotice(long index)
        {
            Index = index;
        }
    }

    private sealed class CameraState(string id, CameraDetectionOptions options)
    {
        public string Id { get; } = id;
        public int FrameInterval { get; } = options.FrameInterval;
        public RoiInfo Roi { get; } = new()
        {
            nX = options.ROI.X, nY = options.ROI.Y,
            nWidth = options.ROI.Width, nHeight = options.ROI.Height
        };
        public FrameNotice? Latest;
        // 只保护回调中的帧号判断和通知提交，不在锁内识别，也不与相机生命周期锁共用。
        public object NoticeSync { get; } = new();
        public long? FirstFrameIndex;
        public long? LastReceivedFrameIndex;
    }

    public void Register(string cameraId, int camId, CameraDetectionOptions options)
    {
        if (options.FrameInterval < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "识别帧间隔必须大于等于 1。");
        cameraDitc[camId] = new CameraState(cameraId, options);
    }

    /// <summary>
    /// 由相机帧回调调用，只保存最新的符合帧间隔条件的通知，不在回调中执行识别。
    /// 新通知覆盖尚未处理的旧通知，避免识别速度跟不上相机帧率时积压。
    /// </summary>
    /// <param name="camId">C++ SDK 返回的相机 ID。</param>
    /// <param name="frameIndex">用于筛选触发通知的帧序号，不代表 SDK 实际识别的图像编号。</param>
    public void TryNotify(int camId, long frameIndex)
    {
        // 只有已登记的相机才接收通知；尚未登记或已注销的相机直接忽略。
        if (cameraDitc.TryGetValue(camId, out var camera))
        {
            // 避免同一相机的并发回调交叉修改起点和待处理通知；锁内只有轻量状态操作。
            lock (camera.NoticeSync)
            {
                if (camera.LastReceivedFrameIndex == frameIndex) return;
                // 第一帧立即提交；帧号回退时按新一轮处理，重新打开也会创建全新状态。
                if (!camera.FirstFrameIndex.HasValue || frameIndex < camera.LastReceivedFrameIndex)
                    camera.FirstFrameIndex = frameIndex;
                camera.LastReceivedFrameIndex = frameIndex;

                // 使用无符号差值避免两个 long 帧号相减溢出；起点为 1、间隔为 5 时选中 1、6、11……。
                ulong offset = unchecked((ulong)frameIndex - (ulong)camera.FirstFrameIndex.Value);
                if (offset % (ulong)camera.FrameInterval != 0) return;

                // 创建本次帧通知，并原子替换 Latest（替换过程不会被其他线程拆开执行）。
                // 后台识别线程会同时读取并清空 Latest，因此使用 Interlocked 协调访问。
                // Exchange 会返回旧通知，这里不需要它：只保留最新一条，不排队。
                Interlocked.Exchange(ref camera.Latest, new FrameNotice(frameIndex));
            }
        }
    }

    public void Unregister(int camId)
    {
        // 停止接收通知，等待正在执行的原生识别返回，随后才能关相机。
        cameraDitc.TryRemove(camId, out _);
        _execution.Wait();
        _execution.Release();
    }

    public async Task RunAsync(CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            throw new InvalidOperationException("识别调度器已在运行。");
        try
        {
            // 10ms 是检查周期，不是识别间隔；识别耗时期间不会另外启动一轮。
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
            while (await timer.WaitForNextTickAsync(token))
            {
                foreach (var (camId, camera) in cameraDitc.ToArray())
                {
                    await _execution.WaitAsync(token);
                    try
                    {
                        // 快照中的相机可能已注销或重新打开。
                        if (!cameraDitc.TryGetValue(camId, out var current) || current != camera) continue;
                        // 回调已按帧间隔筛选，必须有新通知才识别；取出后清空，避免重复处理。
                        var frame = Interlocked.Exchange(ref camera.Latest, null);
                        if (frame is null) continue;

                        token.ThrowIfCancellationRequested();
                        //获取的是一个高精度计时器当前的时间戳（计数值）
                        var started = Stopwatch.GetTimestamp();
                        var result = await detector.LaneDetectAsync(camId, camera.Roi, token);
                        logger.LogDebug("相机 {CameraId} 识别完成，触发帧序号 {TriggerFrameIndex}，结果数 {Count}，耗时 {ElapsedMs:F1}ms",
                            camera.Id, frame.Index, result.count, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception exception)
                    {
                        logger.LogError(exception, "相机 {CameraId} 识别失败", camera.Id);
                    }
                    finally { _execution.Release(); }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { Volatile.Write(ref _running, 0); }
    }
}
