# Native SDK

按无人机项目的写法，结构体、回调委托和全部 7 个原生函数集中在 `NativeInterop/NativeMethods.cs`，命名与 `RobotCamXApi.h`、`RobotCamXData.h` 一致。业务直接调用 `NativeMethods.RobotX_*`，不需要注册服务或配置动态库解析器。

## 调用方式

下面是放在相机业务类中的调用示例，未接入启动流程。回调委托保存为实例字段，持有该实例直到原生端确认不再回调；回调内应捕获业务异常，不能让异常跨越原生边界。

```csharp
private readonly AvFrameIndexFunc _frameCallback = OnFrameIndex;
private readonly AvStatusFunc _statusCallback = OnStatus;
private readonly AvMediaFinishFunc _finishCallback = OnMediaFinished;
private int _camId = -1;
private int _recId = -1;

public int OpenCamera(string address, string alias)
{
    var cfg = new CamCfg { chDev = address, chAlias = alias };
    return NativeMethods.RobotX_OpenCam(ref cfg, _frameCallback, _statusCallback, ref _camId);
}

// 相机连接成功后，由业务按录像开关调用。
public int StartRecording(string path, int seconds)
{
    var media = new MediaInfo { chPath = path };
    return NativeMethods.RobotX_StartRealTimeRecord(
        _camId, ref media, seconds, _finishCallback, ref _recId);
}

public int Detect(RoiInfo roi, out LaneDetectGroup result)
{
    result = new LaneDetectGroup { infos = new LaneDetectInfo[NativeMethods.MAX_COUNT] };
    return NativeMethods.RobotX_LaneDetect(_camId, in roi, ref result);
}

private static void OnFrameIndex(int id, long frmIdx) { /* 处理帧序号 */ }
private static void OnStatus(int id, int status) { /* 处理连接状态 */ }
private static void OnMediaFinished(int id, string fileName, long startTime, long endTime)
{
    /* 处理已完成的录像文件 */
}
```

模型初始化直接调用 `NativeMethods.RobotX_InitModel(modelPath)`；清除结果调用 `NativeMethods.RobotX_ClearDetectResultInfo(camId)`。停止已开始的录像调用 `NativeMethods.RobotX_StopRealTimeRecord(recId)`，再关闭已打开的相机 `NativeMethods.RobotX_CloseCam(camId)`。每次检查原生返回值后再执行后续操作；检测成功后只读取 `infos` 中前 `count` 项，并检查 count 在 0～10 范围内。

## 类型与生命周期

- Linux ARM64 接口使用 Cdecl，包括回调；不照搬无人机示例里的 StdCall。`int&` 对应 `ref int`，`int64_t` 对应 C# `long`。
- 默认结构体大小：CamCfg 400、MediaInfo 200、RoiInfo 16、LaneDetectInfo 20、LaneDetectGroup 204 字节。实际库需与头文件采用一致布局。
- Linux 字符串按 UTF-8 封送。调用方应检查地址、别名、录像路径不含空字符，且 UTF-8 编码长度不超过 199 字节，避免固定数组截断；模型路径也不应包含空字符。
- AvStreamFunc 只有类型定义，没有注册入口，不自行读取或释放 AVPacket。
- 回调时间单位、线程、关闭后是否仍回调以及模型初始化生命周期，需与 SDK 实现方确认后接入相机管理。CameraManager 当前仍为待接入状态。

## 部署与更新

目前 `linux-arm64/LibRobotCamX.so` 为 0 字节占位文件，尚不能实际调用。收到真实 Linux ARM64 库后覆盖该文件；项目会将库复制到输出和发布目录。运行时由 .NET 按 DllImport 名称加载。

1. 更新 `.so` 和配套头文件，若签名变化，直接修改 `NativeInterop/NativeMethods.cs` 和业务调用。
2. 若库名变化，修改 `NativeMethods.DllName`；不再使用 `NativeSdk` 配置节点。
3. 执行 `dotnet build`，通过 `Properties/PublishProfiles/linux-arm64.pubxml` 发布。
4. 将发布目录复制到开发板，执行 `ldd ./LibRobotCamX.so` 检查依赖，再验证相机、录像、检测和回调。编译通过不代表原生调用验证通过。
