namespace TrackedVehicle.Model.Dto;

/// <summary>
/// 结束一次巡检的请求。
/// </summary>
public sealed class FinishInspectionRequest
{
    /// <summary>巡检结束时间；不填写时使用服务器当前时间。</summary>
    /// <example>2026-09-18T18:00:00+08:00</example>
    public DateTime? EndTime { get; set; }
}
