using Microsoft.Extensions.Options;
using TrackedVehicle.Model;

namespace TrackedVehicle.Core;

/// <summary>单例控制中心，统一调度相机和 PLC，由后台服务驱动。</summary>
public sealed class VehicleControlCenter
{
    private readonly ILogger<VehicleControlCenter> _logger;
    private int _running;
    private readonly VehicleOptions _options;
    private readonly DetectionScheduler _detectionScheduler;

    public IReadOnlyList<ICameraManager> Cameras { get; }
    public PLCManager PLC { get; }
    public IDetectManager Detect { get; }

    public VehicleControlCenter(
        IOptions<VehicleOptions> options,
        CameraManagerFactory cameraFactory,
        PLCManager plcManager,
        IDetectManager detectManager,
        DetectionScheduler detectionScheduler,
        ILogger<VehicleControlCenter> logger)
    {
        _logger = logger;
        _options = options.Value;
        _detectionScheduler = detectionScheduler;
        PLC = plcManager;
        Detect = detectManager;
        Cameras = options.Value.Cameras
            .Where(camera => camera.Enabled)
            .Select(cameraFactory.Create)
            .ToList().AsReadOnly();
    }

    /// <summary>遍历启用的相机并启动管理流程，收到取消信号后统一关闭。</summary>
    public async Task RunAsync(CancellationToken stoppingToken)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            throw new InvalidOperationException("控制中心已在运行，不能重复启动。");
        }

        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        // 分别启动，互不等待。原生初始化和开相机可能同步耗时，因此放到后台线程。
        var plcTask = Task.Run(() => RunPlcAsync(lifetime.Token));
        var cameraTask = Task.Run(() => OpenCamerasAsync(lifetime.Token));
        var detectionTask = Task.Run(() => RunDetectionAsync(lifetime.Token));
        try
        {
            _logger.LogInformation("控制中心启动，共 {CameraCount} 个相机管理实例", Cameras.Count);
            // 控制中心的生命周期由宿主决定，不由某个设备任务是否完成决定。
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 宿主正常停止。
        }
        finally
        {
            lifetime.Cancel();
            // 等开相机和正在执行的识别结束，再关闭相机。
            await Task.WhenAll(plcTask, cameraTask, detectionTask);
            foreach (var camera in Cameras.Reverse())
            {
                try
                {
                    await camera.CloseAsync(CancellationToken.None);
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "关闭相机 {CameraId} 失败", camera.Id);
                }
            }
            Interlocked.Exchange(ref _running, 0);
            _logger.LogInformation("控制中心已停止");
        }
    }

    private async Task RunPlcAsync(CancellationToken token)
    {
        try
        {
            await PLC.RunAsync(token);
            if (!token.IsCancellationRequested)
                _logger.LogWarning("PLC 通信已退出");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.LogError(exception, "PLC 通信异常退出");
        }
    }

    private async Task OpenCamerasAsync(CancellationToken token)
    {
        // 开相机不需要等待模型；某台打开失败也继续打开其他相机。
        foreach (var camera in Cameras)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                await camera.OpenAsync(token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception exception)
            {
                _logger.LogError(exception, "打开相机 {CameraId} 失败", camera.Id);
            }
        }
    }

    private async Task RunDetectionAsync(CancellationToken token)
    {
        if (!_options.Cameras.Any(camera => camera.Enabled && camera.DetectionEnabled))
            return;

        try
        {
            // 只有识别依赖模型初始化。此时相机可以已经打开并持续提交最新通知。
            await Detect.InitModelAsync(_options.DetectionModelPath, token);
            await _detectionScheduler.RunAsync(token);
            if (!token.IsCancellationRequested)
                _logger.LogWarning("识别调度已退出");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _logger.LogError(exception, "模型初始化或识别调度失败");
        }
    }
}
