# Native SDK

`linux-arm64` 包含用于 RK3588 的 C++ 算法库及其头文件。

`SDK/LibDemoNative.cs` 是人工维护的 C# P/Invoke 声明，依据 `linux-arm64/include/demo_api.h` 和 `demo_data.h` 编写。项目没有自动生成器，构建不会生成或更新此文件。

## 更新 SDK

1. 更新 `Native/linux-arm64` 下的 `.so` 和配套头文件。
2. 对比新旧头文件，检查导出函数、结构体及回调定义是否变化。
3. 若原生 ABI 变化，手动修改 `SDK/LibDemoNative.cs`：核对导出名称、调用约定、参数类型与传递方式、结构体字段顺序与大小、固定数组长度、字符串封送及回调签名，确保与头文件一致。
4. 将业务逻辑和兼容处理保留在 `Core/Impl/NativeAlgorithmService.cs`，并同步调整受接口变化影响的调用代码。
5. 在项目目录执行 `dotnet build` 检查编译，再使用 `Properties/PublishProfiles/linux-arm64.pubxml` 发布到 `linux-arm64`。
6. 将整个发布目录复制到开发板，在启动服务前执行 `ldd ./libDemo.so` 检查依赖，并在目标设备上验证相机、录像、检测和回调等实际调用。编译通过不能代替 ABI 和运行验证。

若仅库文件名变化且 ABI 保持兼容，更新 `appsettings.json` 中的 `NativeSdk:LibraryName` 即可，无需修改 P/Invoke 声明。
