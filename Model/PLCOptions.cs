namespace TrackedVehicle.Model;

/// <summary>PLC 原始 TCP 连接参数，修改后重启生效。</summary>
public sealed class PLCOptions
{
    public bool Enabled { get; set; }
    public string IP { get; set; } = string.Empty;
    public int Port { get; set; } = 8080;
    public int ConnectTimeoutSeconds { get; set; } = 5;
    public int ReconnectIntervalSeconds { get; set; } = 5;
    public PLCSimulationOptions Simulation { get; set; } = new();
}

/// <summary>仅模拟 PLC 收发，仍使用真实相机和模型；修改后重启生效。</summary>
public sealed class PLCSimulationOptions
{
    public bool Enabled { get; set; }
    public string ReceiveHex { get; set; } = "00 03 01 01 01 00 01 63";
    public int IntervalMilliseconds { get; set; } = 1000;

    /// <summary>启动时解析一次，允许十六进制字节之间有空白，报文必须恰好 8 字节。</summary>
    public byte[] ParseReceiveData()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(IntervalMilliseconds);
        ArgumentException.ThrowIfNullOrWhiteSpace(ReceiveHex);
        var data = Convert.FromHexString(string.Concat(ReceiveHex.Where(character => !char.IsWhiteSpace(character))));
        if (data.Length != 8)
            throw new ArgumentException("PLC 模拟接收报文必须恰好为 8 字节。", nameof(ReceiveHex));
        return data;
    }
}
