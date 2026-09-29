# Native SDK

按无人机项目的写法，结构体、回调委托和全部 7 个原生函数集中在 `NativeInterop/NativeMethods.cs`，命名与 `RobotCamXApi.h`、`RobotCamXData.h` 一致。业务直接调用 `NativeMethods.RobotX_*`，不需要注册服务或配置动态库解析器。

## 调用方式

相机打开、关闭已通过 CameraManager 接入控制中心启动和停止流程；启用检测后由 DetectionScheduler 调度方向定位检测，录像由业务传入已存库的巡检 ID 后启动。下面演示底层接口的调用方式。回调委托保存为实例字段，持有该实例直到原生端确认不再回调；回调内应捕获业务异常，不能让异常跨越原生边界。

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

public int Detect(RoiInfo roi, out OrientationPosInfo result)
{
    result = new OrientationPosInfo();
    return NativeMethods.RobotX_OrientationPosDetect(_camId, in roi, ref result);
}

private static void OnFrameIndex(int id, long frmIdx) { /* 处理帧序号 */ }
private static void OnStatus(int id, int status) { /* 处理连接状态 */ }
private static void OnMediaFinished(int id, string fileName, long startTime, long endTime)
{
    /* 处理已完成的录像文件 */
}
```

模型初始化直接调用 `NativeMethods.RobotX_InitModel(modelPath)`；清除结果调用 `NativeMethods.RobotX_ClearDetectResultInfo(camId)`。停止已开始的录像调用 `NativeMethods.RobotX_StopRealTimeRecord(recId)`，再关闭已打开的相机 `NativeMethods.RobotX_CloseCam(camId)`。每次检查原生返回值后再执行后续操作；检测成功后读取 `centerX`；头文件未约定其坐标基准、单位和无目标标记，不自行换算或以 0 判断有效性。开始录像返回 0 表示成功，录像 ID 从 `recId` 输出参数读取。

## 类型与生命周期

- Linux ARM64 接口使用 Cdecl，包括回调；不照搬无人机示例里的 StdCall。`int&` 对应 `ref int`，`int64_t` 对应 C# `long`。
- 默认结构体大小：CamCfg 400、MediaInfo 200、RoiInfo 16、DetectInfo 20、OrientationPosInfo 4 字节。实际库需与头文件采用一致布局。
- Linux 字符串按 UTF-8 封送。调用方应检查地址、别名、录像路径不含空字符，且 UTF-8 编码长度不超过 199 字节，避免固定数组截断；模型路径也不应包含空字符。
- AvStreamFunc 只有类型定义，没有注册入口，不自行读取或释放 AVPacket。
- CameraManager 已注册帧序号和状态回调，委托保留在实例字段，关闭成功后也不立即释放委托。帧回调只提交最新通知，关闭前等待正在执行的检测结束。录像完成回调捕获本次巡检 ID，时间暂按 Unix 毫秒转换为本地 DateTime，仍需设备联调确认。

## 部署与更新

目前 `linux-arm64/LibRobotCamX.so` 已替换为真实动态库；项目会将库复制到输出和发布目录。运行时由 .NET 按 DllImport 名称加载。

`linux-arm64/robot.rknn` 也会复制到输出和发布目录的 `models` 子目录。`Vehicle.DetectionModelPath` 配置为 `models`，相对路径按程序所在目录解析，也可填写开发板上的绝对路径。初始化前检查模型目录存在，并记录传给 SDK 的完整目录；目录参数不带模型文件名；是否能加载、是否与板上 RKNN 运行库兼容，仍需设备验证。相机的 `DetectionEnabled` 设为 `true` 后才会进入模型初始化和识别流程。

1. 更新 `.so` 和配套头文件，若签名变化，直接修改 `NativeInterop/NativeMethods.cs` 和业务调用。
2. 若库名变化，修改 `NativeMethods.DllName`；不再使用 `NativeSdk` 配置节点。
3. 执行 `dotnet build`，通过 `Properties/PublishProfiles/linux-arm64.pubxml` 发布。
4. 将发布目录复制到开发板，执行 `ldd ./LibRobotCamX.so` 检查依赖，再验证相机、录像、检测和回调。编译通过不代表原生调用验证通过。

本次库的 ELF 头为 64 位小端 AArch64，动态符号表包含头文件声明的全部 7 个 `RobotX_*` 入口。直接依赖包括 `libopencv_world.so.409`、`libcrypto.so.1.1`、`librknnrt.so`、`librockchip_mpp.so.1`、`libavcodec.so.60`、`libavformat.so.60`、`libavutil.so.58`、`libswscale.so.7`、`libswresample.so.4`，以及系统 C/C++、线程、数学和动态加载库。开发板需提供这些依赖及其传递依赖，具体缺项以板上 `ldd` 输出为准。
