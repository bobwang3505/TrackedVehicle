using SqlSugar;

namespace TrackedVehicle.Model;

/// <summary>
/// 一次完整的巡检任务记录。
/// </summary>
[SugarTable("inspection_records")]
public sealed class InspectionRecord
{
    /// <summary>雪花算法生成的主键。</summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false, IsNullable = false)]
    public long Id { get; set; }

    /// <summary>巡检开始时间。</summary>
    [SugarColumn(IsNullable = false)]
    public DateTime StartTime { get; set; }

    /// <summary>巡检结束时间；尚未结束时为空。</summary>
    [SugarColumn(IsNullable = true)]
    public DateTime? EndTime { get; set; }

    /// <summary>记录创建时间。</summary>
    [SugarColumn(IsNullable = false)]
    public DateTime CreateTime { get; set; }

    /// <summary>是否已被软删除。</summary>
    [SugarColumn(IsNullable = false)]
    public bool IsDelete { get; set; }
}
