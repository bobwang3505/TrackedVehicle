namespace TrackedVehicle.Model;

/// <summary>
/// 雪花 ID 生成参数。
/// </summary>
public sealed class SnowflakeOptions
{
    /// <summary>设备编号，范围 0-31；同一数据中心内不能重复，同一节点只能运行一个实例。</summary>
    public long WorkerId { get; set; }

    /// <summary>数据中心编号，范围 0-31。</summary>
    public long DataCenterId { get; set; }

    /// <summary>雪花算法起始时间，部署后不要随意修改。</summary>
    public DateTimeOffset Epoch { get; set; }
}
