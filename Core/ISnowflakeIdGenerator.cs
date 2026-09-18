namespace TrackedVehicle.Core;

/// <summary>
/// 生成全局唯一的雪花 ID。
/// </summary>
public interface ISnowflakeIdGenerator
{
    /// <summary>生成下一个 ID。</summary>
    long NextId();
}
