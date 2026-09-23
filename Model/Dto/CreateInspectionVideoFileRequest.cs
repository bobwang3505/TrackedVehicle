using System.ComponentModel.DataAnnotations;

namespace TrackedVehicle.Model.Dto;

/// <summary>
/// 登记一个已录制完成的巡检视频切片的请求。
/// </summary>
public sealed class CreateInspectionVideoFileRequest
{
    /// <summary>配置中的相机字符串业务 Id，例如 001，不是 SDK 的 camId。</summary>
    [Required]
    [MaxLength(100)]
    public string CameraId { get; set; } = string.Empty;

    /// <summary>切片实际录像开始时间，必须提供，不能以入库时间代替。</summary>
    [Required]
    public DateTime? StartTime { get; set; }

    /// <summary>切片实际录像结束时间，必须提供且不能早于开始时间。</summary>
    [Required]
    public DateTime? EndTime { get; set; }

    /// <summary>视频文件在 RK3588 板载磁盘中的完整路径。</summary>
    /// <example>/data/tracked-vehicle/videos/20260918/segment-001.mp4</example>
    [Required]
    [MaxLength(1024)]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>文件创建时间；不填写时使用服务器当前时间。</summary>
    public DateTime? CreateTime { get; set; }
}
