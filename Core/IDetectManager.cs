using TrackedVehicle.NativeInterop;

namespace TrackedVehicle.Core;

/// <summary>模型和轨道检测管理；camId 是原生相机编号，调用期间应保持相机打开。</summary>
public interface IDetectManager
{
    /// <summary>初始化检测模型；成功后才能调用轨道检测。</summary>
    /// <param name="modelPath">运行 SDK 的设备上的模型路径。</param>
    /// <param name="cancellationToken">调用 SDK 前检查取消。</param>
    /// <returns>模型初始化完成的任务。</returns>
    Task InitModelAsync(string modelPath, CancellationToken cancellationToken);

    /// <summary>在指定相机的区域内执行轨道检测；调用方需保证整个调用期间相机保持打开。</summary>
    /// <param name="camId">相机的 NativeCameraId，不是配置中的业务 Id。</param>
    /// <param name="roi">检测区域：nX、nY 不能为负数，nWidth、nHeight 必须大于 0。</param>
    /// <param name="cancellationToken">调用 SDK 前检查取消。</param>
    /// <returns>检测结果，infos 数组仅前 count 项有效。</returns>
    Task<LaneDetectGroup> LaneDetectAsync(int camId, RoiInfo roi, CancellationToken cancellationToken);

    /// <summary>清除指定相机的 SDK 检测结果，不关闭相机。</summary>
    /// <param name="camId">相机的 NativeCameraId，不是配置中的业务 Id。</param>
    /// <param name="cancellationToken">调用 SDK 前检查取消。</param>
    /// <returns>结果清理完成的任务。</returns>
    Task ClearDetectResultAsync(int camId, CancellationToken cancellationToken);
}
