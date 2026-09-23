using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Options;
using SqlSugar;
using TrackedVehicle.Model;

namespace TrackedVehicle.Core;

/// <summary>扫描未上传文件，按配置的文件并发数上传到 RustFS。</summary>
public sealed class VideoUploadBackgroundService : BackgroundService
{
    private readonly ISqlSugarClient _database;
    private readonly VideoUploadQueue _queue;
    private readonly VideoUploadOptions _options;
    private readonly ILogger<VideoUploadBackgroundService> _logger;

    public VideoUploadBackgroundService(ISqlSugarClient database, VideoUploadQueue queue,
        IOptions<VideoUploadOptions> options, ILogger<VideoUploadBackgroundService> logger)
    {
        _database = database;
        _queue = queue;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        using var client = new AmazonS3Client(
            new BasicAWSCredentials(_options.AccessKey, _options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = _options.ServiceUrl,
                AuthenticationRegion = _options.Region,
                ForcePathStyle = true
            });
        // 每个文件的分片串行发送；总的文件并发数由固定消费者数量控制。
        using var transfer = new TransferUtility(client, new TransferUtilityConfig { ConcurrentServiceRequests = 1 });
        // 调用 ScanAsync，开始扫描流程。把它返回的 Task 放进列表，方便后面等待。
        var tasks = new List<Task> { ScanAsync(stoppingToken) };
        // 启动两个消费者。
        for (int i = 0; i < _options.MaxConcurrency; i++)
            tasks.Add(ConsumeAsync(transfer, stoppingToken));
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        // 停止时先等待所有消费者退出再释放客户端；未完成记录下次启动重新扫描。
    }

    private async Task ScanAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // 按 ID 游标分批扫描，避免一次加载全部历史记录，失败文件也不会挡住后面的文件。
                long lastId = 0;
                while (!token.IsCancellationRequested)
                {
                    var ids = await _database.Queryable<InspectionVideoFile>()
                        .Where(file => file.Id > lastId && !file.IsDelete)
                        // 兼容旧 SQLite 数据中的 NULL 和空字符串；不修改已有数据库数据。
                        .Where("(IsUploaded = 0 OR IsUploaded IS NULL OR IsUploaded = '')")
                        .OrderBy(file => file.Id).Select(file => file.Id).Take(200).ToListAsync();
                    if (ids.Count == 0) break;
                    foreach (long id in ids)
                    {
                        // 每处理一个 ID，先检查停止信号；已取消时抛出异常，退出扫描。
                        token.ThrowIfCancellationRequested();
                        // 队列满时扫描器异步等待；消费者取走一个 ID 腾出空位后，扫描器才能继续入队。
                        // 等的是队列空位，不是这个文件上传成功；等待期间消费者仍可继续上传。
                        // 如果 ID 已被其他任务占用（等待入队、排队或上传中），EnqueueAsync 会根据 _pending 跳过。
                        await _queue.EnqueueAsync(id, token);
                    }
                    // 记录本批最后一个 ID，下一批从它后面继续查询。
                    lastId = ids[ids.Count - 1];
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                _logger.LogError(exception, "扫描未上传视频失败，下一轮扫描重试");
            }
            // 单位为秒，失败后不立即重新入队，避免故障时持续占用网络和数据库。
            // 每隔60秒重新扫描一次
            await Task.Delay(TimeSpan.FromSeconds(_options.ScanIntervalSeconds), token);
        }
    }

    private async Task ConsumeAsync(TransferUtility transfer, CancellationToken token)
    {
        // 每次成功读到一个 id，它就已从 Channel 队列移除并腾出空位，然后才进入循环体处理。
        // 一次取一个；队列为空时异步等待新任务，不会一次清空队列。
        // 出队后 _pending 仍保留该 ID，防止上传期间重复入队，直到 finally 中 Complete 才移除。
        // 上传失败不会自动放回队列，未上传记录由后续数据库扫描重新入队。
        await foreach (long id in _queue.ReadAllAsync(token))
        {
            try
            {
                token.ThrowIfCancellationRequested();
                // 入队后记录可能已删除或上传完成，消费时再次检查。
                var file = await _database.Queryable<InspectionVideoFile>()
                    .Where(item => item.Id == id && !item.IsDelete)
                    .Where("(IsUploaded = 0 OR IsUploaded IS NULL OR IsUploaded = '')")
                    .Select(item => new { item.Id, item.InspectionRecordId, item.FilePath, item.S3Key })
                    .FirstAsync();
                if (file is null) continue;
                if (!File.Exists(file.FilePath))
                    throw new FileNotFoundException("待上传录像文件不存在。", file.FilePath);

                // 每条记录使用固定 key，上传成功但回写失败后重试仍使用同一对象地址。
                string key = string.IsNullOrWhiteSpace(file.S3Key)
                    ? $"inspections/{file.InspectionRecordId}/{file.Id}{Path.GetExtension(file.FilePath)}"
                    : file.S3Key;
                await transfer.UploadAsync(file.FilePath, _options.BucketName, key, token);
                // 上传确认成功后，一条 UPDATE 同时写入 key 和成功标记；失败不能提前标记。
                await _database.Updateable<InspectionVideoFile>()
                    .SetColumns(item => new InspectionVideoFile { S3Key = key, IsUploaded = true })
                    .Where(item => item.Id == id && !item.IsDelete)
                    .ExecuteCommandAsync();
                _logger.LogInformation("视频 {FileId} 上传完成，S3 key：{S3Key}", id, key);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                _logger.LogError(exception, "视频 {FileId} 上传或状态回写失败，保留未上传状态供扫描重试", id);
            }
            finally { _queue.Complete(id); }
        }
    }
}
