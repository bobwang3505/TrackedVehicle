namespace TrackedVehicle.Model;

/// <summary>RustFS 的 S3 接口和后台上传配置，修改后重启生效。</summary>
public sealed class VideoUploadOptions
{
    public bool Enabled { get; set; }
    public string ServiceUrl { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public string BucketName { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public int MaxConcurrency { get; set; } = 2;
    public int QueueCapacity { get; set; } = 100;
    public int ScanIntervalSeconds { get; set; } = 60;
}
