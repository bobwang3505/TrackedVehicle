namespace TrackedVehicle.Model;

/// <summary>PLC 原始 TCP 连接参数，修改后重启生效。</summary>
public sealed class PLCOptions
{
    public bool Enabled { get; set; }
    public string IP { get; set; } = string.Empty;
    public int Port { get; set; } = 8080;
    public int ConnectTimeoutSeconds { get; set; } = 5;
    public int ReconnectIntervalSeconds { get; set; } = 5;
}
