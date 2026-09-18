using Serilog;

namespace TrackedVehicle.Extensions;

/// <summary>
/// 文件日志注册扩展。
/// </summary>
public static class LoggingExtensions
{
    /// <summary>
    /// 在保留默认控制台日志的同时，增加按天滚动的文件日志。
    /// </summary>
    public static WebApplicationBuilder AddFileLogging(this WebApplicationBuilder builder)
    {
        var relativePath = builder.Configuration["FileLogging:Path"] ?? "Logs/tracked-vehicle-.log";
        var logPath = Path.GetFullPath(relativePath, builder.Environment.ContentRootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);

        var retainedFileCountLimit = builder.Configuration.GetValue<int?>(
            "FileLogging:RetainedFileCountLimit") ?? 30;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: retainedFileCountLimit,
                shared: true)
            .CreateLogger();

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();
        builder.Logging.AddSerilog(Log.Logger, dispose: true);

        return builder;
    }
}
