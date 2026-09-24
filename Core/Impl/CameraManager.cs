using System.Text;
using TrackedVehicle.Model;
using TrackedVehicle.NativeInterop;

namespace TrackedVehicle.Core.Impl;

/// <summary>一个实例管理一个相机，由控制中心持有并管理生命周期。</summary>
public sealed class CameraManager : ICameraManager
{
    private readonly CameraOptions _options;
    private readonly ILogger<CameraManager> _logger;
    // 同一个相机共用这把锁：多个线程调用打开、关闭、录像等操作时，互斥执行锁内代码。
    // readonly 保证锁对象不会被替换；每个 CameraManager 各有一把锁，不会锁住其他相机。
    // 离开 lock 时自动释放锁（包括 return 或抛异常）；未使用这把锁的代码不受它保护。
    private readonly object _sync = new();
    // 关闭失败时仍保留委托，避免原生端调用已被 GC 回收的委托。
    private readonly AvFrameIndexFunc _frameCallback;
    private readonly DetectionScheduler _detectionScheduler;
    private readonly AvStatusFunc _statusCallback;
    private readonly RecordingFileRegistrationService _recordingFiles;
    // 每次录像的委托捕获固定巡检 ID。SDK 未声明停止后绝无迟到回调，因此在此管理器存活期间保留委托。
    // 不能开始下一趟时直接替换并释放旧委托，否则原生端仍持有的函数指针可能失效。
    private readonly List<AvMediaFinishFunc> _mediaFinishCallbacks = new();
    private int? _camId;
    private int? _recordId;
    private long? _recordInspectionId;

    public CameraManager(CameraOptions options, ILogger<CameraManager> logger, DetectionScheduler detectionScheduler,
        RecordingFileRegistrationService recordingFiles)
    {
        _options = options;
        _logger = logger;
        _detectionScheduler = detectionScheduler;
        _frameCallback = OnFrameIndex;
        _statusCallback = OnStatus;
        _recordingFiles = recordingFiles;
    }

    public string Id => _options.Id;

    public int NativeCameraId
    {
        get
        {
            // 读取句柄时也使用同一把锁；返回后锁已释放，不保证后续使用期间相机不会关闭。
            lock (_sync)
                return _camId ?? throw new InvalidOperationException($"相机 {Id} 尚未打开。");
        }
    }

    public Task<int> StartRecordingAsync(long inspectionId, string path, CancellationToken cancellationToken)
    {
        // 检查录像状态、调用 SDK、保存录像 ID 一起加锁，避免重复录像或与关闭操作冲突。
        // NativeCameraId 内部也会加锁；同一线程可以再次进入同一把锁，不会把自己锁住。
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (inspectionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(inspectionId), "必须传入已创建的巡检记录 ID。");
            var camId = NativeCameraId;
            if (_recordId.HasValue)
            {
                int existingId = _recordId.Value;
                if (_recordInspectionId != inspectionId)
                    throw new InvalidOperationException($"相机 {Id} 正在为巡检 {_recordInspectionId} 录像，请先停止再切换巡检。");
                return Task.FromResult(existingId);
            }

            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ValidateNativeString(path, "录像路径");
            if (_options.Record.SegmentSeconds <= 0)
                throw new InvalidOperationException("录像分段秒数必须大于 0。");

            var mediaInfo = new MediaInfo { chPath = path };
            var recordId = -1;
            // 在调用 SDK 前创建并保留委托，兼容启动调用尚未返回就发生回调的情况。
            // 通过闭包绑定本次参数，不读取可变的“当前巡检”，迟到的旧回调仍属于原来的巡检。
            AvMediaFinishFunc callback = (id, fileName, startTime, endTime) =>
                OnMediaFinish(inspectionId, id, fileName, startTime, endTime);

            _mediaFinishCallbacks.Add(callback);
            var result = NativeMethods.RobotX_StartRealTimeRecord(camId, ref mediaInfo,
                _options.Record.SegmentSeconds, callback, ref recordId);
            if (result < 0)
                throw new InvalidOperationException($"相机 {Id} 开始录像失败，SDK 返回码：{result}。");

            // SDK 返回值也是有效录像编号，兼容未填写输出参数的实现。
            _recordId = recordId >= 0 ? recordId : result;
            _recordInspectionId = inspectionId;
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
        // 调用方 StopRecordingAsync / CloseAsync 已持有 _sync，这里复用它们的保护。
        if (_recordId is not int recordId)
            return;

        var result = NativeMethods.RobotX_StopRealTimeRecord(recordId);
        if (result != 0)
            throw new InvalidOperationException($"相机 {Id} 停止录像失败，SDK 返回码：{result}。");

        _recordId = null;
        _recordInspectionId = null;
        _logger.LogInformation("相机 {CameraId} 已停止录像，录像 ID：{RecordId}", Id, recordId);
    }

    /// <summary>打开原生相机并注册回调；重复打开不重复创建相机。</summary>
    public Task OpenAsync(CancellationToken cancellationToken)
    {
        // 把“检查是否打开 → 打开 SDK 相机 → 保存 ID”作为一个整体保护。
        // 若 A、B 同时调用，先拿到锁的 A 打开后，B 才能进入，并因已有 ID 直接返回。
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
            if (_options.DetectionEnabled)
            {
                _detectionScheduler.Register(Id, camId, _options.Detection);
            }
            _logger.LogInformation("相机 {CameraId}（{CameraName}）打开请求成功，原生 ID：{NativeCameraId}，连接结果由状态回调报告",
                Id, _options.CameraName, camId);
        }
        return Task.CompletedTask;
    }

    /// <summary>关闭成功后清除原生 ID；未打开或已关闭时不调用 SDK。</summary>
    public Task CloseAsync(CancellationToken cancellationToken)
    {
        // 停止录像和关闭相机共用同一把锁，防止中途另一个线程又开始录像。
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_camId is not int camId)
                return Task.CompletedTask;

            if (_options.DetectionEnabled)
                _detectionScheduler.Unregister(camId);
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

    /// <summary>
    /// 开相机的回调
    /// </summary>
    /// <param name="id">相机id _camId</param>
    /// <param name="frameIndex">帧序号（图像的编号）</param>
    private void OnFrameIndex(int id, long frameIndex)
    {
        try
        {
            if (_options.DetectionEnabled)
                _detectionScheduler.TryNotify(id, frameIndex);
        }
        catch (Exception)
        {
            // 不获取相机锁、不调用 SDK；托管异常不能跨越原生回调边界。
        }
    }

    private void OnMediaFinish(long inspectionId, int id, string fileName, long startTime, long endTime)
    {
        // 不获取生命周期锁；分段完成不代表整个录像会话结束。
        try
        {
            // 暂按 Unix 毫秒时间戳解释：从 1970-01-01 UTC 起累计的毫秒数。
            // 转为本地 DateTime，与当前 DateTime.Now 的存库时间口径一致；设备联调时核对。
            var startedAt = DateTimeOffset.FromUnixTimeMilliseconds(startTime).LocalDateTime;
            var endedAt = DateTimeOffset.FromUnixTimeMilliseconds(endTime).LocalDateTime;
            // 回调只提交已完成切片的信息，不获取 _sync，不等待存库，也不按切片创建后台任务。
            if (!_recordingFiles.TryNotifyCompleted(inspectionId, fileName, Id, startedAt, endedAt))
            {
                _logger.LogError("录像完成通知未接收，需补登记：巡检 {InspectionId}，相机 {CameraId}，文件 {FileName}，开始 {StartTime:O}，结束 {EndTime:O}",
                    inspectionId, Id, fileName, startedAt, endedAt);
                return;
            }
            _logger.LogInformation("相机 {CameraId} 录像分段完成，原生 ID：{NativeId}，文件：{FileName}，开始：{StartTime}，结束：{EndTime}",
                Id, id, fileName, startedAt, endedAt);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            // 转换失败时保留原始值，便于联调确认 SDK 时间格式；日志异常也不能跨越原生边界。
            try
            {
                _logger.LogError(exception, "相机 {CameraId} 录像时间无法按 Unix 毫秒转换，文件：{FileName}，原始开始：{StartTime}，原始结束：{EndTime}",
                    Id, fileName, startTime, endTime);
            }
            catch (Exception) { }
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
