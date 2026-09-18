namespace TrackedVehicle.Model;

/// <summary>
/// C++ 原生算法库配置。
/// </summary>
public sealed class NativeSdkOptions
{
    /// <summary>是否启用原生算法库。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>发布目录中的动态库文件名。</summary>
    public string LibraryName { get; set; } = "libDemo.so";

    /// <summary>默认摄像头设备或地址。</summary>
    public string DefaultDevice { get; set; } = "/dev/video0";

    /// <summary>默认摄像头别名。</summary>
    public string DefaultAlias { get; set; } = "tracked-vehicle";
}
