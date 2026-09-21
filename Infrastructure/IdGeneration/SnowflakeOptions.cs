namespace TrackedVehicle.Infrastructure.IdGeneration;

/// <summary>
/// 雪花 ID 生成参数。
/// </summary>
public sealed class SnowflakeOptions
{
    /// <summary>Yitter 设备编号，范围 0-63；所有实例之间必须唯一，同一编号只能运行一个实例。</summary>
    public long WorkerId { get; set; }

    /// <summary>雪花算法起始时间，部署后不要随意修改。</summary>
    public DateTimeOffset Epoch { get; set; }
}
