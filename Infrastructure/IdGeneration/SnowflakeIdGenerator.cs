using Microsoft.Extensions.Options;
using Yitter.IdGenerator;

namespace TrackedVehicle.Infrastructure.IdGeneration;

/// <summary>单例持有 Yitter 雪花漂移 ID 生成器。</summary>
internal sealed class SnowflakeIdGenerator : IIdGenerator
{
    private readonly DefaultIdGenerator _generator;

    public SnowflakeIdGenerator(IOptions<SnowflakeOptions> options)
    {
        var value = options.Value;
        if (value.WorkerId is < 0 or > 63)
        {
            throw new InvalidOperationException("Snowflake:WorkerId 必须在 0-63 之间。");
        }

        if (value.Epoch < DateTimeOffset.UtcNow.AddYears(-50) || value.Epoch > DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("Snowflake:Epoch 必须是最近 50 年内已经发生的时间。");
        }

        _generator = new DefaultIdGenerator(new IdGeneratorOptions((ushort)value.WorkerId)
        {
            Method = 1,
            BaseTime = value.Epoch.UtcDateTime,
            WorkerIdBitLength = 6,
            SeqBitLength = 6
        });
    }

    public long Create() => _generator.NewLong();
}
