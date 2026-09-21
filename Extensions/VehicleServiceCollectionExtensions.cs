using TrackedVehicle.Core;
using TrackedVehicle.Core.Impl;
using TrackedVehicle.Model;

namespace TrackedVehicle.Extensions;

/// <summary>控制中心与设备管理的依赖注入配置。</summary>
public static class VehicleServiceCollectionExtensions
{
    public static IServiceCollection AddVehicleControl(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VehicleOptions>()
            .Bind(configuration.GetRequiredSection(VehicleOptions.SectionName))
            .Validate(options => options.Cameras is not null && options.Cameras.All(camera =>
                camera is not null && !string.IsNullOrWhiteSpace(camera.Id)
                && camera.Port is > 0 and <= 65535
                && camera.Record is not null && camera.Record.FPS > 0 && camera.Record.SegmentSeconds > 0),
                "相机必须配置 Id、有效端口、正数 FPS 和录像切片时长。")
            .Validate(options => options.Cameras is not null && options.Cameras.All(camera => camera is not null)
                && options.Cameras.Select(camera => camera.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == options.Cameras.Count,
                "相机 Id 不能重复。")
            .Validate(options => options.PLC is not null && (!options.PLC.Enabled ||
                (!string.IsNullOrWhiteSpace(options.PLC.IP) && options.PLC.Port is > 0 and <= 65535
                && options.PLC.ConnectTimeoutSeconds is > 0 and <= 3600
                && options.PLC.ReconnectIntervalSeconds is > 0 and <= 3600)),
                "启用 PLC 时必须配置地址、有效端口以及 1-3600 秒的连接超时和重连间隔。")
            .ValidateOnStart();

        services.AddSingleton<CameraManagerFactory>();
        services.AddSingleton<IDetectManager, DetectManager>();
        services.AddSingleton<PLCManager>();
        services.AddSingleton<VehicleControlCenter>();
        services.AddHostedService<TrackedVehicleBackgroundService>();
        return services;
    }
}
