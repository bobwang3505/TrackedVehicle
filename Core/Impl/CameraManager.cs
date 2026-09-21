using System.Text;
using TrackedVehicle.Model;

namespace TrackedVehicle.Core.Impl;

/// <summary>一个实例管理一个相机，由控制中心持有并管理生命周期。</summary>
public sealed class CameraManager : ICameraManager
{
    private readonly CameraOptions _options;
    private readonly ILogger<CameraManager> _logger;
    private readonly object _sync = new();
    // 关闭失败时仍保留委托，避免原生端调用已被 GC 回收的委托。
    private readonly AvFrameIndexFunc _frameCallback;
    private readonly AvStatusFunc _statusCallback;
    private readonly AvMediaFinishFunc _mediaFinishCallback;
    private int? _camId;
    private int? _recordId;

    public CameraManager(CameraOptions options, ILogger<CameraManager> logger)
    {
        _options = options;
        _logger = logger;
        _frameCallback = OnFrameIndex;
        _statusCallback = OnStatus;
        _mediaFinishCallback = OnMediaFinish;
    }

    public string Id => _options.Id;

    public int NativeCameraId
    {
        get
        {
            lock (_sync)
                return _camId ?? throw new InvalidOperationException($"相机 {Id} 尚未打开。");
        }
    }

    public Task<int> StartRecordingAsync(string path, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var camId = NativeCameraId;
            if (_recordId is int existingId)
                return Task.FromResult(existingId);

            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ValidateNativeString(path, "录像路径");
            if (_options.Record.SegmentSeconds <= 0)
                throw new InvalidOperationException("录像分段秒数必须大于 0。");

            var mediaInfo = new MediaInfo { chPath = path };
            var recordId = -1;
            var result = NativeMethods.RobotX_StartRealTimeRecord(camId, ref mediaInfo,
                _options.Record.SegmentSeconds, _mediaFinishCallback, ref recordId);
            if (result < 0)
                throw new InvalidOperationException($"相机 {Id} 开始录像失败，SDK 返回码：{result}。");

            // SDK 返回值也是有效录像编号，兼容未填写输出参数的实现。
            _recordId = recordId >= 0 ? recordId : result;
            _logger.LogInformation("相机 {CameraId} 开始录像，录像 ID：{RecordId}", Id, _recordId);
            return Task.FromResult(_recordId.Value);
        }
    }

    public Task StopRecordingAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StopRecording();
        }
        return Task.CompletedTask;
    }

    private void StopRecording()
    {
        if (_recordId is not int recordId)
            return;

        var result = NativeMethods.RobotX_StopRealTimeRecord(recordId);
        if (result != 0)
            throw new InvalidOperationException($"相机 {Id} 停止录像失败，SDK 返回码：{result}。");

        _recordId = null;
        _logger.LogInformation("相机 {CameraId} 已停止录像，录像 ID：{RecordId}", Id, recordId);
    }

    /// <summary>打开原生相机并注册回调；重复打开不重复创建相机。</summary>
    public Task OpenAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_camId.HasValue)
                return Task.CompletedTask;

            var cfg = new CamCfg { chDev = BuildAddress(), chAlias = _options.CameraName };
            ValidateNativeString(cfg.chDev, "相机地址");
            ValidateNativeString(cfg.chAlias, "相机别名");
            var camId = -1;
            var result = NativeMethods.RobotX_OpenCam(ref cfg, _frameCallback, _statusCallback, ref camId);
            if (result != 0)
                throw new InvalidOperationException($"打开相机 {Id} 失败，SDK 返回码：{result}。");

            _camId = camId;
            _logger.LogInformation("相机 {CameraId}（{CameraName}）打开请求成功，原生 ID：{NativeCameraId}，连接结果由状态回调报告",
                Id, _options.CameraName, camId);
        }
        return Task.CompletedTask;
    }

    /// <summary>关闭成功后清除原生 ID；未打开或已关闭时不调用 SDK。</summary>
    public Task CloseAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_camId is not int camId)
                return Task.CompletedTask;

            StopRecording();
            var result = NativeMethods.RobotX_CloseCam(camId);
            if (result != 0)
                throw new InvalidOperationException($"关闭相机 {Id} 失败，SDK 返回码：{result}。");

            _camId = null;
            _logger.LogInformation("相机 {CameraId} 已关闭，原生 ID：{NativeCameraId}", Id, camId);
        }
        return Task.CompletedTask;
    }

    private string BuildAddress()
    {
        var address = _options.CameraIP.Trim();
        if (string.IsNullOrWhiteSpace(address))
            throw new InvalidOperationException($"相机 {Id} 未配置地址。");

        // 完整地址保留厂商要求的码流路径、查询参数和认证信息。
        if (address.Contains("://", StringComparison.Ordinal))
            return address;

        var uri = new UriBuilder("rtsp", address, _options.Port);
        if (!string.IsNullOrEmpty(_options.UserName))
        {
            uri.UserName = Uri.EscapeDataString(_options.UserName);
            uri.Password = Uri.EscapeDataString(_options.Pwd);
        }
        return uri.Uri.AbsoluteUri;
    }

    private static void ValidateNativeString(string value, string name)
    {
        if (value.Contains('\0') || Encoding.UTF8.GetByteCount(value) > 199)
            throw new InvalidOperationException($"{name}不能包含空字符，且 UTF-8 编码长度不能超过 199 字节。");
    }

    private static void OnFrameIndex(int id, long frameIndex)
    {
        // 帧处理由后续业务流程接入。
    }

    private void OnMediaFinish(int id, string fileName, long startTime, long endTime)
    {
        // 不获取生命周期锁；分段完成不代表整个录像会话结束。
        try
        {
            _logger.LogInformation("相机 {CameraId} 录像分段完成，原生 ID：{NativeId}，文件：{FileName}，开始：{StartTime}，结束：{EndTime}",
                Id, id, fileName, startTime, endTime);
        }
        catch (Exception)
        {
            // 托管异常不能跨越原生回调边界。
        }
    }

    private void OnStatus(int id, int status)
    {
        // 不获取生命周期锁，避免 SDK 等待回调造成死锁。
        try
        {
            if (status == 1)
                _logger.LogInformation("相机 {CameraId} 连接成功，原生 ID：{NativeCameraId}", Id, id);
            else
                _logger.LogWarning("相机 {CameraId} 状态变化，原生 ID：{NativeCameraId}，状态码：{Status}", Id, id, status);
        }
        catch (Exception)
        {
            // 托管异常不能跨越原生回调边界。
        }
    }
}
