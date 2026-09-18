namespace TrackedVehicle.Model.Dto;

/// <summary>
/// 打开摄像头请求。
/// </summary>
public sealed class OpenCameraRequest
{
    /// <summary>摄像头设备路径或网络地址；不填则使用配置值。</summary>
    /// <example>/dev/video0</example>
    public string? Device { get; set; }

    /// <summary>摄像头别名；不填则使用配置值。</summary>
    /// <example>front-camera</example>
    public string? Alias { get; set; }

    /// <summary>父摄像头 ID。</summary>
    public int ParentCameraId { get; set; }
}
