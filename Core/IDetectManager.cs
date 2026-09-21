namespace TrackedVehicle.Core;

/// <summary>模型和轨道检测管理；camId 是原生相机编号，调用期间应保持相机打开。</summary>
public interface IDetectManager
{
    Task InitModelAsync(string modelPath, CancellationToken cancellationToken);
    Task<LaneDetectGroup> LaneDetectAsync(int camId, RoiInfo roi, CancellationToken cancellationToken);
    Task ClearDetectResultAsync(int camId, CancellationToken cancellationToken);
}
