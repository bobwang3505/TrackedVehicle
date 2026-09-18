using Microsoft.Extensions.Options;
using TrackedVehicle.Model;

namespace TrackedVehicle.Core.Impl;

/// <summary>
/// 线程安全的 64 位雪花 ID 生成器。
/// </summary>
public sealed class SnowflakeIdGenerator : ISnowflakeIdGenerator
{
    private const int WorkerIdBits = 5;
    private const int DataCenterIdBits = 5;
    private const int SequenceBits = 12;
    private const long MaxWorkerId = (1L << WorkerIdBits) - 1;
    private const long MaxDataCenterId = (1L << DataCenterIdBits) - 1;
    private const long SequenceMask = (1L << SequenceBits) - 1;
    private const int WorkerIdShift = SequenceBits;
    private const int DataCenterIdShift = SequenceBits + WorkerIdBits;
    private const int TimestampShift = SequenceBits + WorkerIdBits + DataCenterIdBits;

    private readonly object _lock = new();
    private readonly long _workerId;
    private readonly long _dataCenterId;
    private readonly long _epochMilliseconds;
    private long _lastTimestamp = -1;
    private long _sequence;

    public SnowflakeIdGenerator(IOptions<SnowflakeOptions> options)
    {
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
        _epochMilliseconds = value.Epoch.ToUnixTimeMilliseconds();

        if (_epochMilliseconds <= 0 || _epochMilliseconds > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
        {
            throw new InvalidOperationException("Snowflake:Epoch 必须是已经发生的有效 UTC 时间。");
        }
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
                _sequence = (_sequence + 1) & SequenceMask;
                if (_sequence == 0)
                {
                    timestamp = WaitForNextMillisecond(timestamp);
                }
            }
            else
            {
                _sequence = 0;
            }

            _lastTimestamp = timestamp;

            return ((timestamp - _epochMilliseconds) << TimestampShift)
                   | (_dataCenterId << DataCenterIdShift)
                   | (_workerId << WorkerIdShift)
                   | _sequence;
        }
    }

    private static long CurrentTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static long WaitForNextMillisecond(long timestamp)
    {
        var current = CurrentTimestamp();
        while (current <= timestamp)
        {
            Thread.SpinWait(10);
            current = CurrentTimestamp();
        }

        return current;
    }
}
