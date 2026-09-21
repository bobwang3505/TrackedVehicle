using Microsoft.Extensions.DependencyInjection.Extensions;
using TrackedVehicle.Infrastructure.IdGeneration;

namespace TrackedVehicle.Extensions;

/// <summary>ID 生成器注册扩展。</summary>
public static class IdGenerationServiceCollectionExtensions
{
    /// <summary>绑定 Snowflake 配置并注册单例雪花 ID 生成器。</summary>
    public static IServiceCollection AddSnowflakeIdGeneration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<SnowflakeOptions>(configuration.GetSection("Snowflake"));
        services.TryAddSingleton<IIdGenerator, SnowflakeIdGenerator>();
        return services;
    }
}
