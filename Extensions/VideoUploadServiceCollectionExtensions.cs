using TrackedVehicle.Core;
using TrackedVehicle.Model;

namespace TrackedVehicle.Extensions;

public static class VideoUploadServiceCollectionExtensions
{
    public static IServiceCollection AddVideoUpload(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<VideoUploadOptions>().Bind(configuration.GetSection("VideoUpload"))
            .Validate(options => options.QueueCapacity > 0 && options.MaxConcurrency is > 0 and <= 32
                && options.ScanIntervalSeconds > 0, "上传队列容量、扫描间隔须为正数，文件上传并发数须为 1-32。")
            .Validate(options => !options.Enabled ||
                (Uri.TryCreate(options.ServiceUrl, UriKind.Absolute, out var uri)
                && (uri.Scheme == "http" || uri.Scheme == "https")
                && !string.IsNullOrWhiteSpace(options.Region)
                && !string.IsNullOrWhiteSpace(options.BucketName)
                && !string.IsNullOrWhiteSpace(options.AccessKey)
                && !string.IsNullOrWhiteSpace(options.SecretKey)),
                "启用上传时必须配置 RustFS S3 接口地址、区域、bucket 和访问凭据。")
            .ValidateOnStart();
        services.AddSingleton<VideoUploadQueue>();
        services.AddHostedService<VideoUploadBackgroundService>();
        services.AddSingleton<RecordingFileRegistrationService>();
        services.AddHostedService(provider => provider.GetRequiredService<RecordingFileRegistrationService>());
        return services;
    }
}
