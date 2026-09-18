using System.ComponentModel.DataAnnotations;

namespace TrackedVehicle.Model.Dto;

/// <summary>
/// 调用原生 SDK 开始录像的请求。
/// </summary>
public sealed class StartRecordingRequest
{
    /// <summary>录像文件保存路径。</summary>
    /// <example>/data/tracked-vehicle/videos/demo.mp4</example>
    [Required]
    [MaxLength(199)]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>单个录像切片时长，单位秒。</summary>
    [Range(1, 86400)]
    public int DurationSeconds { get; set; } = 60;
}
