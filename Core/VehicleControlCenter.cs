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

    public VehicleControlCenter(
        IOptions<VehicleOptions> options,
        CameraManagerFactory cameraFactory,
        PLCManager plcManager,
        ILogger<VehicleControlCenter> logger)
    {
        _logger = logger;
        PLC = plcManager;
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

        try
        {
            _logger.LogInformation("控制中心启动，共 {CameraCount} 个相机管理实例；PLC 等待后续接入", Cameras.Count);
            foreach (var camera in Cameras)
            {
                await camera.OpenAsync(stoppingToken);
            }

            // 真实 SDK 接入后在此调度连接状态检查和失败重试，不重复打开正常工作的相机。
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 宿主正常停止。
        }
        finally
        {
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
