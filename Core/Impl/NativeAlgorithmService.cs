using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Options;
using TrackedVehicle.Model;
using TrackedVehicle.SDK;

namespace TrackedVehicle.Core.Impl;

/// <summary>
/// C++ 摄像与算法动态库适配器。
/// </summary>
public sealed class NativeAlgorithmService : INativeAlgorithmService
{
    private readonly NativeSdkOptions _options;
    private readonly ILogger<NativeAlgorithmService> _logger;
    private readonly string _libraryPath;

    // 原生代码可能在调用返回后继续持有回调地址，因此必须由长生命周期对象持有委托。
    private readonly RobotCamXNative.AvFrameIndexCallback _frameIndexCallback;
    private readonly RobotCamXNative.AvStatusCallback _statusCallback;
    private readonly RobotCamXNative.AvMediaFinishCallback _mediaFinishCallback;

    public NativeAlgorithmService(
        IOptions<NativeSdkOptions> options,
        ILogger<NativeAlgorithmService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _libraryPath = Path.Combine(AppContext.BaseDirectory, _options.LibraryName);

        _frameIndexCallback = OnFrameIndex;
        _statusCallback = OnStatus;
        _mediaFinishCallback = OnMediaFinished;

        NativeLibraryResolver.Initialize(_libraryPath);
    }

    public NativeSdkStatus GetStatus()
    {
        return new NativeSdkStatus(
            _options.Enabled,
            IsSupportedPlatform(),
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            _libraryPath,
            File.Exists(_libraryPath));
    }

    public OpenCameraResult OpenCamera(string? device, string? alias)
    {
        EnsureAvailable();

        var config = new RobotCamXNative.CameraConfig
        {
            Device = string.IsNullOrWhiteSpace(device) ? _options.DefaultDevice : device,
            Alias = string.IsNullOrWhiteSpace(alias) ? _options.DefaultAlias : alias
        };
        ValidateFixedString(config.Device, nameof(device));
        ValidateFixedString(config.Alias, nameof(alias));
        var cameraId = -1;
        var resultCode = RobotCamXNative.OpenCamera(
            ref config,
            _frameIndexCallback,
            _statusCallback,
            ref cameraId);

        _logger.LogInformation(
            "原生 SDK 打开摄像头完成，ResultCode={ResultCode}, CameraId={CameraId}",
            resultCode,
            cameraId);

        return new OpenCameraResult(resultCode, cameraId);
    }

    public int CloseCamera(int cameraId)
    {
        EnsureAvailable();
        var resultCode = RobotCamXNative.CloseCamera(cameraId);
        _logger.LogInformation(
            "原生 SDK 关闭摄像头完成，ResultCode={ResultCode}, CameraId={CameraId}",
            resultCode,
            cameraId);
        return resultCode;
    }

    public StartRecordingResult StartRecording(
        int cameraId,
        string filePath,
        int durationSeconds)
    {
        EnsureAvailable();

        ValidateFixedString(filePath, nameof(filePath));
        var mediaInfo = new RobotCamXNative.MediaInfo { Path = filePath };
        var recordingId = -1;
        var resultCode = RobotCamXNative.StartRealTimeRecord(
            cameraId,
            ref mediaInfo,
            durationSeconds,
            _mediaFinishCallback,
            ref recordingId);

        _logger.LogInformation(
            "原生 SDK 开始录像完成，ResultCode={ResultCode}, RecordingId={RecordingId}",
            resultCode,
            recordingId);

        return new StartRecordingResult(resultCode, recordingId);
    }

    public int StopRecording(int recordingId)
    {
        EnsureAvailable();
        var resultCode = RobotCamXNative.StopRealTimeRecord(recordingId);
        _logger.LogInformation(
            "原生 SDK 停止录像完成，ResultCode={ResultCode}, RecordingId={RecordingId}",
            resultCode,
            recordingId);
        return resultCode;
    }

    public NativeDetectionResult Detect(int cameraId, DetectionRegion region)
    {
        EnsureAvailable();

        var roi = new RobotCamXNative.RoiInfo
        {
            X = region.X,
            Y = region.Y,
            Width = region.Width,
            Height = region.Height
        };
        var nativeResult = new RobotCamXNative.LaneDetectGroup
        {
            Detections = new RobotCamXNative.LaneDetectInfo[RobotCamXNative.MaxDetectionCount]
        };
        var resultCode = RobotCamXNative.LaneDetect(cameraId, in roi, ref nativeResult);
        var count = Math.Clamp(nativeResult.Count, 0, nativeResult.Detections.Length);
        var detections = nativeResult.Detections
            .Take(count)
            .Select(item => new DetectionBox(
                item.BoxX,
                item.BoxY,
                item.BoxWidth,
                item.BoxHeight,
                item.Valid != 0))
            .ToArray();

        return new NativeDetectionResult(resultCode, detections);
    }

    public int InitModel(string modelPath)
    {
        EnsureAvailable();
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        if (modelPath.Contains('\0')) { throw new ArgumentException("模型路径不能包含空字符。", nameof(modelPath)); }
        return RobotCamXNative.InitModel(modelPath);
    }

    public int ClearDetectResultInfo(int cameraId)
    {
        EnsureAvailable();
        return RobotCamXNative.ClearDetectResultInfo(cameraId);
    }

    private static void ValidateFixedString(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Contains('\0') || Encoding.UTF8.GetByteCount(value) > 199)
        {
            throw new ArgumentException("字符串不能包含空字符，且 UTF-8 编码最多 199 字节（保留末尾空字符）。", parameterName);
        }
    }

    private void EnsureAvailable()
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("NativeSdk 已在配置中禁用。");
        }

        if (!IsSupportedPlatform())
        {
            throw new PlatformNotSupportedException(
                "当前动态库仅支持 Linux ARM64，必须在 RK3588 上调用。");
        }

        if (!File.Exists(_libraryPath))
        {
            throw new FileNotFoundException("找不到原生动态库。", _libraryPath);
        }
    }

    private static bool IsSupportedPlatform() =>
        OperatingSystem.IsLinux()
        && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

    private void OnFrameIndex(int id, long frameIndex) =>
        _logger.LogDebug("原生帧回调：CameraId={CameraId}, FrameIndex={FrameIndex}", id, frameIndex);

    private void OnStatus(int id, int status) =>
        _logger.LogInformation("原生状态回调：CameraId={CameraId}, Status={Status}", id, status);

    private void OnMediaFinished(int id, string fileName, long startTime, long endTime) =>
        _logger.LogInformation(
            "原生录像完成回调：RecordingId={RecordingId}, FileName={FileName}, Start={Start}, End={End}",
            id,
            fileName,
            startTime,
            endTime);
}
