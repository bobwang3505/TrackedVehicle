using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using TrackedVehicle.Model;

namespace TrackedVehicle.Core;

/// <summary>只排队已存库的文件 ID；队列满时由下一轮数据库扫描补入。</summary>
public sealed class VideoUploadQueue
{
    private readonly Channel<long> _channel;
    // 记录已申请入队、正在排队或上传中的文件 ID，防止新文件通知和数据库扫描重复提交同一文件。
    // key 是文件记录的 long ID；byte 值固定为 0，仅作占位，只关心 ID 是否存在。
    // TryAdd 是线程安全的：多个线程同时添加同一 ID，只有一个能成功并继续入队。
    // 入队失败或本次上传处理结束（包括失败）后移除 ID，让后续扫描可以重试未上传文件。
    // 此字典只保存在内存中；重启后由数据库扫描恢复任务，不用它判断文件是否已上传成功。
    private readonly ConcurrentDictionary<long, byte> _pending = new();
    private readonly bool _enabled;

    public VideoUploadQueue(IOptions<VideoUploadOptions> options)
    {
        _enabled = options.Value.Enabled;
        // Wait 模式下 TryWrite 遇满返回 false，不会静默丢弃队列中已有的文件。
        _channel = Channel.CreateBounded<long>(new BoundedChannelOptions(options.Value.QueueCapacity)
        {
            // 队列满时，WriteAsync 异步等待空位（可取消）；TryWrite 立即返回 false，不丢弃已有数据。
            FullMode = BoundedChannelFullMode.Wait,
            // 唤醒等待读写的任务时，不在当前读写调用中直接执行其后续代码，避免被对方的处理拖慢。
            AllowSynchronousContinuations = false
        });
    }

    /// <summary>
    /// 尝试将已存库的文件 ID 放入上传队列，不等待队列空位，也不在这里执行上传。
    /// 新切片存库后调用；未能入队的未上传记录可由后续数据库扫描补入。
    /// </summary>
    /// <param name="fileId">已存库的视频文件记录 ID，不是巡检 ID 或相机 camId。</param>
    /// <returns>
    /// true 表示本次成功入队，不代表上传成功；false 表示上传未启用、文件已在处理中，或队列无法接收。
    /// </returns>
    public bool TryEnqueue(long fileId)
    {
        // 先占用这个 ID，阻止其他线程重复提交；未启用时短路返回，不会添加到 _pending。
        if (!_enabled || !_pending.TryAdd(fileId, 0)) return false;
        // 立即尝试写入；成功后保留 _pending 标记，直到消费者完成上传和数据库回写。
        if (_channel.Writer.TryWrite(fileId)) return true;
        // 队列满或已关闭，实际没有入队，必须撤销占用，否则后续扫描也无法重试这个文件。
        _pending.TryRemove(fileId, out _);
        return false;
    }

    /// <summary>供消费者通过 await foreach 逐个取出文件 ID；队列为空时异步等待，上传由消费者执行。</summary>
    internal IAsyncEnumerable<long> ReadAllAsync(CancellationToken token)
        => _channel.Reader.ReadAllAsync(token);

    /// <summary>
    /// 供数据库扫描器异步入队，不是消费队列，也不等待文件上传完成。
    /// 与 TryEnqueue 遇满立即返回不同，本方法会异步等待空位，不占用线程阻塞等待。
    /// 上传未启用或 ID 已被占用时直接返回；否则等待写入成功，取消或写入失败时抛出异常。
    /// </summary>
    /// <param name="fileId">已存库的视频文件记录 ID。</param>
    /// <param name="token">服务停止时用于取消等待入队，避免队列满时一直等下去。</param>
    internal async Task EnqueueAsync(long fileId, CancellationToken token)
    {
        // 等待空位期间也先占用 ID，防止其他线程把同一文件重复提交到队列。
        if (!_enabled || !_pending.TryAdd(fileId, 0)) return;
        // 有空位就写入；队列满时 await 等消费者取走文件 ID 后腾出空位。
        // 写入成功后保留 _pending 标记，由消费者处理结束时调用 Complete 移除。
        try { await _channel.Writer.WriteAsync(fileId, token); }
        catch
        {
            // 取消或写入失败意味着未成功入队，撤销占用并将异常交给扫描器处理。
            _pending.TryRemove(fileId, out _);
            throw;
        }
    }

    // 去重范围包含排队和正在上传，直到本次处理（含数据库回写）结束才释放。
    internal void Complete(long fileId) => _pending.TryRemove(fileId, out _);
}
