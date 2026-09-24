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
    private readonly VideoUploadOptions _options;
    private readonly ILogger<VideoUploadBackgroundService> _logger;

    public VideoUploadBackgroundService(ISqlSugarClient database,
        IOptions<VideoUploadOptions> options, ILogger<VideoUploadBackgroundService> logger)
    {
        _database = database;
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
        // 每个文件的分片串行发送；每批最多启动 MaxConcurrency 个文件上传任务。
        using var transfer = new TransferUtility(client, new TransferUtilityConfig { ConcurrentServiceRequests = 1 });
        try { await ScanAsync(transfer, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        // 扫描会等待本批上传任务全部退出，再释放客户端；未完成记录下次启动重新扫描。
    }

    private async Task ScanAsync(TransferUtility transfer, CancellationToken token)
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
                        .OrderBy(file => file.Id).Select(file => file.Id).Take(_options.MaxConcurrency).ToListAsync();
                    if (ids.Count == 0) break;
                    // 查询后先检查取消，再启动这一批；启动后由 WhenAll 等待所有任务退出。
                    token.ThrowIfCancellationRequested();
                    var tasks = new List<Task>();
                    foreach (long id in ids)
                    {
                        tasks.Add(UploadFileAsync(id, transfer, token));
                    }
                    // 本批全部结束后才查下一批，各批和各轮不会重叠，不需要上传队列或内存去重字典。
                    await Task.WhenAll(tasks);
                    // 记录本批最后一个 ID，下一批从它后面继续查询。
                    lastId = ids[ids.Count - 1];
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                _logger.LogError(exception, "扫描未上传视频失败，下一轮扫描重试");
            }
            // 一轮扫描及上传结束后等待配置的秒数（默认 60），再从 ID 0 开始，重试失败文件。
            await Task.Delay(TimeSpan.FromSeconds(_options.ScanIntervalSeconds), token);
        }
    }

    private async Task UploadFileAsync(long id, TransferUtility transfer, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            // 扫描后记录可能已删除或上传完成，实际上传前再次检查。
            var file = await _database.Queryable<InspectionVideoFile>()
                .Where(item => item.Id == id && !item.IsDelete)
                .Where("(IsUploaded = 0 OR IsUploaded IS NULL OR IsUploaded = '')")
                .Select(item => new { item.Id, item.InspectionRecordId, item.FilePath, item.S3Key })
                .FirstAsync();
            if (file is null) return;
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
    }
}
