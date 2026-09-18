namespace TrackedVehicle.Model.Dto;

/// <summary>
/// 开始一次巡检的请求。
/// </summary>
public sealed class StartInspectionRequest
{
    /// <summary>巡检开始时间；不填写时使用服务器当前时间。</summary>
    /// <example>2026-09-18T17:00:00+08:00</example>
    public DateTime? StartTime { get; set; }
}
