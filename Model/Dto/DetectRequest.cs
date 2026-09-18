namespace TrackedVehicle.Model.Dto;

/// <summary>
/// 调用原生算法检测的请求。
/// </summary>
public sealed class DetectRequest
{
    /// <summary>检测区域左上角 X 坐标。</summary>
    public int X { get; set; }

    /// <summary>检测区域左上角 Y 坐标。</summary>
    public int Y { get; set; }

    /// <summary>检测区域宽度。</summary>
    public int Width { get; set; } = 640;

    /// <summary>检测区域高度。</summary>
    public int Height { get; set; } = 480;
}
