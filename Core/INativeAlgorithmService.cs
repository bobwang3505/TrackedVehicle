using TrackedVehicle.Model;

namespace TrackedVehicle.Core;

/// <summary>
/// C++ 摄像与算法动态库的稳定业务接口。
/// </summary>
public interface INativeAlgorithmService
{
    /// <summary>获取当前平台和动态库状态，不加载动态库。</summary>
    NativeSdkStatus GetStatus();

    /// <summary>打开摄像头。</summary>
    OpenCameraResult OpenCamera(string? device, string? alias);

    /// <summary>关闭摄像头，返回原生结果码。</summary>
    int CloseCamera(int cameraId);

    /// <summary>开始录像。</summary>
    StartRecordingResult StartRecording(
        int cameraId,
        string filePath,
        int durationSeconds);

    /// <summary>停止录像，返回原生结果码。</summary>
    int StopRecording(int recordingId);

    /// <summary>初始化轨道检测模型，返回原生结果码。</summary>
    int InitModel(string modelPath);

    /// <summary>清除相机检测结果，返回原生结果码。</summary>
    int ClearDetectResultInfo(int cameraId);

    /// <summary>调用轨道检测算法检测指定区域。</summary>
    NativeDetectionResult Detect(int cameraId, DetectionRegion region);
}
