using TrackedVehicle.Model;

namespace TrackedVehicle.Core.Impl;

/// <summary>一个实例管理一个相机，由控制中心持有并管理生命周期。</summary>
public sealed class CameraManager(
    CameraOptions options,
    ILogger<CameraManager> logger) : ICameraManager
{
    public string Id => options.Id;

    /// <summary>打开相机的接入位置；等待真实动态库到位后接入。</summary>
    public Task OpenAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // TODO：接入真实 C++ 开相机接口，并保存句柄和回调委托，防止委托被 GC 回收。
        // TODO：按真实 SDK 的回调协议接入录像；Record.Enabled 决定是否录像。
        // TODO：DetectionEnabled 决定是否识别；明确原生图像包生命周期后再投递后台处理。
        logger.LogInformation("相机 {CameraId}（{CameraName}）等待真实 SDK 接入，尚未打开；识别开关：{DetectionEnabled}，录像开关：{RecordEnabled}",
            options.Id, options.CameraName, options.DetectionEnabled, options.Record.Enabled);
        return Task.CompletedTask;
    }

    /// <summary>关闭相机的接入位置。</summary>
    public Task CloseAsync(CancellationToken cancellationToken)
    {
        // TODO：停止接收回调、停止录像、关闭原生句柄，再释放委托及相关资源。
        logger.LogInformation("相机 {CameraId} 管理流程结束（当前未接入真实 SDK）", options.Id);
        return Task.CompletedTask;
    }
}
