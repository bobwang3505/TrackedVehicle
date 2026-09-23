using System.Threading.Channels;
using Microsoft.Extensions.Options;
using SqlSugar;
using TrackedVehicle.Model;
using TrackedVehicle.Model.Dto;

namespace TrackedVehicle.Core;

/// <summary>接收录像完成通知，后台登记文件；暂不绑定 SDK 回调和巡检触发时机。</summary>
public sealed class RecordingFileRegistrationService : BackgroundService
{
    private sealed class CompletedFile
    {
        public long InspectionId { get; }
        public string FilePath { get; }
        public string CameraId { get; }
        public DateTime StartTime { get; }
        public DateTime EndTime { get; }
        public DateTime CreateTime { get; } = DateTime.Now;

        public CompletedFile(long inspectionId, string filePath, string cameraId, DateTime startTime, DateTime endTime)
        {
            InspectionId = inspectionId;
            FilePath = filePath;
            CameraId = cameraId;
            StartTime = startTime;
            EndTime = endTime;
        }
    }

    private readonly Channel<CompletedFile> _notices;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VideoUploadQueue _uploadQueue;
    private readonly ILogger<RecordingFileRegistrationService> _logger;

    public RecordingFileRegistrationService(IServiceScopeFactory scopeFactory, VideoUploadQueue uploadQueue,
        IOptions<VideoUploadOptions> options, ILogger<RecordingFileRegistrationService> logger)
    {
        _scopeFactory = scopeFactory;
        _uploadQueue = uploadQueue;
        _logger = logger;
        _notices = Channel.CreateBounded<CompletedFile>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false
        });
    }

    /// <summary>
    /// 供原生录像完成回调使用，不等待数据库或上传。路径必须指向已完成的本地文件。
    /// 返回 false 表示未接收，调用方必须保留通知并补交，不能当作存库成功。
    /// inspectionId 必须来自该次录像绑定的巡检，不能临时读取“当前巡检”。
    /// cameraId 为配置中的业务 Id；时间须在确认 SDK 单位和时间基准后转换，不能用回调到达时间代替。
    /// </summary>
    public bool TryNotifyCompleted(long inspectionId, string filePath, string cameraId, DateTime startTime, DateTime endTime)
    {
        if (inspectionId <= 0 || string.IsNullOrWhiteSpace(filePath)) return false;
        if (string.IsNullOrWhiteSpace(cameraId) || cameraId.Length > 100 || endTime < startTime) return false;
        return _notices.Writer.TryWrite(new CompletedFile(inspectionId, filePath.Trim(), cameraId.Trim(), startTime, endTime));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var notice in _notices.Reader.ReadAllAsync(stoppingToken))
            {
                // 当前通知存库失败时保留并重试；只有存库成功后才处理下一条，不静默丢弃。
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var database = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
                        // 单消费者串行登记，同一巡检、同一相机、同一路径的重复完成通知复用已有记录。
                        // 文件路径必须每个切片唯一，不能覆盖旧文件后重复使用同一路径。
                        var existingId = await database.Queryable<InspectionVideoFile>()
                            .Where(file => file.InspectionRecordId == notice.InspectionId
                                && file.CameraId == notice.CameraId
                                && file.FilePath == notice.FilePath)
                            .Select(file => file.Id).FirstAsync();
                        if (existingId != 0)
                        {
                            _uploadQueue.TryEnqueue(existingId);
                            break;
                        }
                        var inspection = scope.ServiceProvider.GetRequiredService<IInspectionService>();
                        var file = await inspection.AddVideoFileAsync(notice.InspectionId,
                            new CreateInspectionVideoFileRequest
                            {
                                FilePath = notice.FilePath,
                                CameraId = notice.CameraId,
                                StartTime = notice.StartTime,
                                EndTime = notice.EndTime,
                                CreateTime = notice.CreateTime
                            });
                        if (file is null)
                            _logger.LogError("录像文件 {FilePath} 无法登记：巡检 {InspectionId} 不存在或已删除，请核对并补登记",
                                notice.FilePath, notice.InspectionId);
                        break;
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        _logger.LogError(exception, "录像文件 {FilePath} 登记失败，5 秒后重试", notice.FilePath);
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // 先拒绝新通知并尽量排空已接收通知，再取消消费者；宿主的停止超时仍然有效。
        _notices.Writer.TryComplete();
        try
        {
            if (ExecuteTask is not null) await ExecuteTask.WaitAsync(cancellationToken);
        }
        finally { await base.StopAsync(cancellationToken); }
    }
}
