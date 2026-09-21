using Microsoft.Extensions.Options;
using TrackedVehicle.Model;

namespace TrackedVehicle.Core;

/// <summary>单例控制中心，统一调度相机和 PLC，由后台服务驱动。</summary>
public sealed class VehicleControlCenter
{
    private readonly ILogger<VehicleControlCenter> _logger;
    private int _running;

    public IReadOnlyList<ICameraManager> Cameras { get; }
    public PLCManager PLC { get; }
    public IDetectManager Detect { get; }

    public VehicleControlCenter(
        IOptions<VehicleOptions> options,
        CameraManagerFactory cameraFactory,
        PLCManager plcManager,
        IDetectManager detectManager,
        ILogger<VehicleControlCenter> logger)
    {
        _logger = logger;
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
        var plcTask = PLC.RunAsync(lifetime.Token);
        try
        {
            _logger.LogInformation("控制中心启动，共 {CameraCount} 个相机管理实例，PLC 通信循环已调度", Cameras.Count);
            foreach (var camera in Cameras)
            {
                await camera.OpenAsync(stoppingToken);
            }

            // 相机由 SDK 回调报告连接状态；保持运行直到停止，不重复打开相机。
            await plcTask;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 宿主正常停止。
        }
        finally
        {
            lifetime.Cancel();
            try
            {
                await plcTask;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "PLC 通信任务退出异常");
            }

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
}
