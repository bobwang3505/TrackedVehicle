using SqlSugar;

namespace TrackedVehicle.Model;

/// <summary>
/// 巡检过程中生成的本地视频切片文件。
/// </summary>
[SugarTable("inspection_video_files")]
[SugarIndex("idx_inspection_video_files_record_id", nameof(InspectionRecordId), OrderByType.Asc)]
public sealed class InspectionVideoFile
{
    /// <summary>雪花算法生成的主键。</summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = false, IsNullable = false)]
    public long Id { get; set; }

    /// <summary>所属巡检记录 ID，仅作逻辑关联，不建立物理外键。</summary>
    [SugarColumn(IsNullable = false)]
    public long InspectionRecordId { get; set; }

    /// <summary>视频文件在板载本地磁盘中的路径。</summary>
    [SugarColumn(Length = 1024, IsNullable = false)]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>文件记录创建时间。</summary>
    [SugarColumn(IsNullable = false)]
    public DateTime CreateTime { get; set; }

    /// <summary>是否已被软删除。</summary>
    [SugarColumn(IsNullable = false)]
    public bool IsDelete { get; set; }
}
