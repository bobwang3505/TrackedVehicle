using Microsoft.Extensions.Options;
using TrackedVehicle.Model;

namespace TrackedVehicle.Core.Impl;

/// <summary>
/// 线程安全的秒级雪花 ID 生成器，返回 long，且不超过 JavaScript 安全整数范围。
/// </summary>
public sealed class SnowflakeIdGenerator : ISnowflakeIdGenerator
{
    private const int WorkerIdBits = 5;
    private const int DataCenterIdBits = 5;
    private const int SequenceBits = 10;
    private const long MaxSafeInteger = 9_007_199_254_740_991L;
    private const long MaxWorkerId = (1L << WorkerIdBits) - 1;
    private const long MaxDataCenterId = (1L << DataCenterIdBits) - 1;
    private const long SequenceMask = (1L << SequenceBits) - 1;
    private const int WorkerIdShift = SequenceBits;
    private const int DataCenterIdShift = SequenceBits + WorkerIdBits;
    private const int TimestampShift = SequenceBits + WorkerIdBits + DataCenterIdBits;

    private readonly object _lock = new();
    private readonly long _workerId;
    private readonly long _dataCenterId;
    private readonly long _epochSeconds;
    private readonly TimeProvider _timeProvider;
    private long _lastTimestamp;
    private long _sequence;

    public SnowflakeIdGenerator(IOptions<SnowflakeOptions> options, TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        var value = options.Value;

        if (value.WorkerId is < 0 or > MaxWorkerId)
        {
            throw new InvalidOperationException($"Snowflake:WorkerId 必须在 0-{MaxWorkerId} 之间。");
        }

        if (value.DataCenterId is < 0 or > MaxDataCenterId)
        {
            throw new InvalidOperationException($"Snowflake:DataCenterId 必须在 0-{MaxDataCenterId} 之间。");
        }

        _workerId = value.WorkerId;
        _dataCenterId = value.DataCenterId;
        _epochSeconds = value.Epoch.ToUnixTimeSeconds();

        if (_epochSeconds <= 0 || value.Epoch > _timeProvider.GetUtcNow())
        {
            throw new InvalidOperationException("Snowflake:Epoch 必须是已经发生的有效 UTC 时间。");
        }

        // 秒级序号无法跨进程保存；跳过启动所在秒，避免正常重启后重用该秒内的 ID。
        // 同一节点仍只能运行一个实例，跨重启的系统时钟回拨需由部署环境防止。
        _lastTimestamp = CurrentTimestamp();
        _sequence = SequenceMask;
    }

    public long NextId()
    {
        lock (_lock)
        {
            var timestamp = CurrentTimestamp();

            if (timestamp < _lastTimestamp)
            {
                throw new InvalidOperationException("系统时钟发生回拨，暂时无法生成雪花 ID。");
            }

            if (timestamp == _lastTimestamp)
            {
                if (_sequence == SequenceMask)
                {
                    timestamp = WaitForNextSecond(timestamp);
                    _sequence = 0;
                }
                else
                {
                    _sequence++;
                }
            }
            else
            {
                _sequence = 0;
            }

            var elapsedSeconds = timestamp - _epochSeconds;
            if (elapsedSeconds < 0 || elapsedSeconds > (MaxSafeInteger >> TimestampShift))
            {
                throw new InvalidOperationException("雪花 ID 时间超出 JavaScript 安全整数范围，停止生成。");
            }

            _lastTimestamp = timestamp;

            return (elapsedSeconds << TimestampShift)
                   | (_dataCenterId << DataCenterIdShift)
                   | (_workerId << WorkerIdShift)
                   | _sequence;
        }
    }

    private long CurrentTimestamp() => _timeProvider.GetUtcNow().ToUnixTimeSeconds();

    private long WaitForNextSecond(long timestamp)
    {
        var current = CurrentTimestamp();
        while (current <= timestamp)
        {
            if (current < timestamp)
            {
                throw new InvalidOperationException("系统时钟发生回拨，暂时无法生成雪花 ID。");
            }

            Thread.Sleep(1);
            current = CurrentTimestamp();
        }

        return current;
    }
}
