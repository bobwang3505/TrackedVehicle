namespace TrackedVehicle.Core;

/// <summary>单个相机的管理接口，每个相机使用独立实例。</summary>
public interface ICameraManager
{
    /// <summary>配置中的相机业务编号，与原生 SDK 句柄不同。</summary>
    string Id { get; }

    /// <summary>已打开相机的原生 SDK 编号；未打开时抛异常。检测调用期间应保持相机打开。</summary>
    int NativeCameraId { get; }

    /// <summary>绑定已存库的巡检 ID 并开始分段录像；同一巡检重复调用返回现有编号，不允许录制中切换巡检。</summary>
    Task<int> StartRecordingAsync(long inspectionId, string path, CancellationToken cancellationToken);

    /// <summary>停止当前录像；未录像时不调用 SDK。</summary>
    Task StopRecordingAsync(CancellationToken cancellationToken);

    /// <summary>调用原生 SDK 打开相机并注册帧序号、连接状态回调。</summary>
    Task OpenAsync(CancellationToken cancellationToken);

    /// <summary>关闭相机并清理相关资源。</summary>
    Task CloseAsync(CancellationToken cancellationToken);
}
