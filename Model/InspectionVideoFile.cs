using SqlSugar;

namespace TrackedVehicle.Model;

/// <summary>
/// 巡检过程中已录制完成的本地视频切片文件，录制完成后才登记入库。
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

    /// <summary>S3 对象 key；上传成功后填写，尚未上传时为空，不保存临时下载链接。</summary>
    [SugarColumn(Length = 1024, IsNullable = true)]
    public string? S3Key { get; set; }

    /// <summary>是否已成功上传；上传成功后与 S3Key 一起更新，失败时保持 false 以便重试。</summary>
    [SugarColumn(IsNullable = false, DefaultValue = "0")]
    public bool IsUploaded { get; set; }

    /// <summary>文件记录创建时间。</summary>
    [SugarColumn(IsNullable = false)]
    public DateTime CreateTime { get; set; }

    /// <summary>是否已被软删除。</summary>
    [SugarColumn(IsNullable = false)]
    public bool IsDelete { get; set; }
}
