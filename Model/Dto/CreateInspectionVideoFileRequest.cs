using System.ComponentModel.DataAnnotations;

namespace TrackedVehicle.Model.Dto;

/// <summary>
/// 登记一个已录制完成的巡检视频切片的请求。
/// </summary>
public sealed class CreateInspectionVideoFileRequest
{
    /// <summary>视频文件在 RK3588 板载磁盘中的完整路径。</summary>
    /// <example>/data/tracked-vehicle/videos/20260918/segment-001.mp4</example>
    [Required]
    [MaxLength(1024)]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>文件创建时间；不填写时使用服务器当前时间。</summary>
    public DateTime? CreateTime { get; set; }
}
