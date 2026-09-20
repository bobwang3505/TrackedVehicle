namespace TrackedVehicle.Core;

/// <summary>单个相机的管理接口，每个相机使用独立实例。</summary>
public interface ICameraManager
{
    /// <summary>配置中的相机业务编号，与原生 SDK 句柄不同。</summary>
    string Id { get; }

    /// <summary>打开相机；真实 SDK 接入后注册相关回调。</summary>
    Task OpenAsync(CancellationToken cancellationToken);

    /// <summary>关闭相机并清理相关资源。</summary>
    Task CloseAsync(CancellationToken cancellationToken);
}
