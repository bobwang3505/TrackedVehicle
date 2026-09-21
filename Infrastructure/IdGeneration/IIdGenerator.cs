namespace TrackedVehicle.Infrastructure.IdGeneration;

/// <summary>生成 long 类型的唯一 ID。</summary>
public interface IIdGenerator
{
    /// <summary>生成一个新的 ID。</summary>
    long Create();
}
