# Native SDK

`linux-arm64` 包含用于 RK3588 的 C++ 算法库及其头文件。

`SDK/RobotCamXNative.cs` 是人工维护的 C# P/Invoke 声明，依据 `linux-arm64/include/RobotCamXApi.h` 和 `RobotCamXData.h` 编写。项目没有自动生成器，构建不会生成或更新此文件。

## 当前接入状态

当前仅收到头文件，真实的 `LibRobotCamX.so` 尚未提供。`Native/linux-arm64/LibRobotCamX.so` 当前是 0 字节占位文件，不能加载或用于实际调用；收到真实库后直接覆盖此文件。库文件名区分大小写，现有项目规则会将其复制到输出及发布目录（占位文件也会被复制）。编译无需真实库，实际调用需要有效的 Linux ARM64 库及其依赖。

已声明全部 7 个导出函数：`RobotX_OpenCam`、`RobotX_CloseCam`、`RobotX_StartRealTimeRecord`、`RobotX_StopRealTimeRecord`、`RobotX_InitModel`、`RobotX_LaneDetect`、`RobotX_ClearDetectResultInfo`。

- 使用 Cdecl 调用约定。`int&` 映射为 `ref int`，`int64_t` 映射为 C# `long`。
- 按头文件默认布局，CameraConfig 为 400 字节（已移除父相机编号）、MediaInfo 为 200 字节、RoiInfo 为 16 字节、LaneDetectInfo 为 20 字节、LaneDetectGroup 为 204 字节（10 个结果和计数）。实际二进制仍需确认未使用额外打包选项。
- 字符串暂按 Linux UTF-8 约定处理，固定 `char[200]` 最多容纳 199 字节内容，适配器检查字节长度；编码需与 C++ 实现确认。
- 新 OpenCam 仅接收帧序号和状态回调，没有码流回调参数。数据头文件虽然声明 AvStreamFunc，但尚无对应注册入口；不自行读取或释放 AVPacket。
- 录像完成回调由 StartRealTimeRecord 注册，并非 OpenCam 参数。后续可在相机连接成功后发起录像，但需要确认状态回调、线程和停止契约后再接入管理流程。
- 录像回调的时间单位和 id 含义、关闭后是否还有回调、模型初始化生命周期，均需由 SDK 实现方确认。暂不补造释放模型接口。

旧 Demo 库及其头文件已移除。现有 NativeAlgorithmService 已同步签名，新增模型初始化及清除结果方法；CameraManager 仍保留待接入状态，不自动调用尚未提供的库。

## 更新 SDK

1. 更新 `Native/linux-arm64` 下的 `.so` 和配套头文件。
2. 对比新旧头文件，检查导出函数、结构体及回调定义是否变化。
3. 若原生 ABI 变化，手动修改 `SDK/RobotCamXNative.cs`：核对导出名称、调用约定、参数类型与传递方式、结构体字段顺序与大小、固定数组长度、字符串封送及回调签名，确保与头文件一致。
4. 将业务逻辑和兼容处理保留在 `Core/Impl/NativeAlgorithmService.cs`，并同步调整受接口变化影响的调用代码。
5. 在项目目录执行 `dotnet build` 检查编译，再使用 `Properties/PublishProfiles/linux-arm64.pubxml` 发布到 `linux-arm64`。
6. 将整个发布目录复制到开发板，在启动服务前执行 `ldd ./LibRobotCamX.so` 检查依赖，并在目标设备上验证相机、录像、检测和回调等实际调用。编译通过不能代替 ABI 和运行验证。

若仅库文件名变化且 ABI 保持兼容，更新 `appsettings.json` 中的 `NativeSdk:LibraryName` 即可，无需修改 P/Invoke 声明。
