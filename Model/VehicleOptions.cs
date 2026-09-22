namespace TrackedVehicle.Model;

/// <summary>控制中心配置，启动时读取 Vehicle 节点。</summary>
public sealed class VehicleOptions
{
    public const string SectionName = "Vehicle";
    public string RecordSavePath { get; set; } = string.Empty;
    public string DetectionModelPath { get; set; } = string.Empty;
    public PLCOptions PLC { get; set; } = new();
    public List<CameraOptions> Cameras { get; set; } = [];
}

/// <summary>单个相机的配置；Id 是业务编号，与原生 SDK 返回的相机句柄不同。</summary>
public sealed class CameraOptions
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string CameraName { get; set; } = string.Empty;
    public string CameraIP { get; set; } = string.Empty;
    public int Port { get; set; } = 554;
    public string UserName { get; set; } = string.Empty;
    public string Pwd { get; set; } = string.Empty;
    public bool DetectionEnabled { get; set; }
    public CameraDetectionOptions Detection { get; set; } = new();
    public CameraRecordOptions Record { get; set; } = new();
}

/// <summary>识别开始时间的最小间隔；实际吞吐量受模型耗时和相机数量限制。</summary>
public sealed class CameraDetectionOptions
{
    public int IntervalMilliseconds { get; set; } = 200;
    public DetectionRoiOptions ROI { get; set; } = new();
}

public sealed class DetectionRoiOptions
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>录像参数；真实 SDK 接入后在开相机回调流程中使用。</summary>
public sealed class CameraRecordOptions
{
    public bool Enabled { get; set; } = true;
    public int FPS { get; set; } = 25;
    public int SegmentSeconds { get; set; } = 60;
}
