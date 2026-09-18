namespace TrackedVehicle.Model;

/// <summary>
/// 原生算法库运行状态。
/// </summary>
public sealed record NativeSdkStatus(
    bool Enabled,
    bool IsSupportedPlatform,
    string OperatingSystem,
    string ProcessArchitecture,
    string LibraryPath,
    bool LibraryFileExists);

/// <summary>
/// 打开摄像头的结果。
/// </summary>
public sealed record OpenCameraResult(int ResultCode, int CameraId);

/// <summary>
/// 开始录像的结果。
/// </summary>
public sealed record StartRecordingResult(int ResultCode, int RecordingId);

/// <summary>
/// 图像检测区域。
/// </summary>
public sealed record DetectionRegion(int X, int Y, int Width, int Height);

/// <summary>
/// 单个算法检测框。
/// </summary>
public sealed record DetectionBox(int X, int Y, int Width, int Height, bool IsValid);

/// <summary>
/// 一次算法检测结果。
/// </summary>
public sealed record NativeDetectionResult(int ResultCode, IReadOnlyList<DetectionBox> Detections);
