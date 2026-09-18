using TrackedVehicle.Core;
using TrackedVehicle.Core.Impl;
using TrackedVehicle.Model;

namespace TrackedVehicle.Extensions;

/// <summary>
/// C++ 原生 SDK 注册扩展。
/// </summary>
public static class NativeSdkServiceCollectionExtensions
{
    /// <summary>
    /// 注册原生算法库配置和长生命周期适配器。
    /// </summary>
    public static IServiceCollection AddNativeSdk(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<NativeSdkOptions>(configuration.GetSection("NativeSdk"));
        services.AddSingleton<INativeAlgorithmService, NativeAlgorithmService>();
        return services;
    }
}
