# TrackedVehicle
巡检小车

## 控制中心与相机管理

启动时由 `TrackedVehicleBackgroundService` 调用单例 `VehicleControlCenter`，读取 `Vehicle` 配置（原示例 `AAA` 节点）。配置修改后需要重启程序。

- `VehicleOptions` 映射保存路径和相机数组，端口、FPS、切片秒数使用数字。
- `CameraManagerFactory` 为每个启用的相机创建独立对象，控制中心持有这些对象；不是多个相机共用一个单例相机对象。
- `CameraManager.OpenAsync` / `CloseAsync` 封装打开、关闭相机；`StartRecordingAsync` / `StopRecordingAsync` 封装开始、停止录像。录像使用 `Record.SegmentSeconds` 分段，完成回调记录文件信息；关闭相机前先停止录像，停止失败保留状态以便重试。
- 同级的 `IDetectManager` / `DetectManager` 注册为单例，通过控制中心的 `Detect` 访问，封装模型初始化、轨道检测和清除结果。检测前需初始化模型；传入 `camera.NativeCameraId`，不能使用配置中的业务 `Id`。调用方需保证检测期间相机不被关闭，结果数组仅前 `count` 项有效。
- `Enabled` 控制是否创建相机实例。录像和检测目前由业务显式调用，不会随启动自动执行；`DetectionEnabled` 和 `Record.Enabled` 仍预留给后续自动调度流程。
- `PLCManager` 注册为单例，控制中心启动时自动运行 TCP 连接、接收和断线重连循环；连接失败不会阻止相机管理流程启动。
- 停止后台服务时控制中心依次调用各相机的关闭方法。相机 Id 重复、端口或录像数字参数无效时启动校验失败。

配置里的相机 IP 和账号仍为示例，实际运行前需要补全。CameraIP 支持完整 RTSP 地址（包含码流路径及认证信息），或使用纯 IP、Port、UserName、Pwd 生成 rtsp://账号:密码@IP:端口/，账号密码进行 URL 转义。地址和别名不能含空字符，且最多 199 个 UTF-8 字节。打开失败会退出控制中心并清理已打开相机；关闭失败记录错误并继续清理其他相机，目前不自动重试。旧的原生 SDK 测试 Controller 已移除，相机启停由控制中心调度；底层声明集中在 `NativeInterop/NativeMethods.cs`，业务直接调用 `NativeMethods.RobotX_*`；结构体、回调及调用示例见 `Native/README.md`。

调用示例（相机已打开，路径为运行 SDK 的设备上的路径）：

```csharp
var camera = controlCenter.Cameras[0];
await camera.StartRecordingAsync("/data/records", cancellationToken);
await controlCenter.Detect.InitModelAsync("/data/models/lane.engine", cancellationToken);
var result = await controlCenter.Detect.LaneDetectAsync(camera.NativeCameraId,
    new RoiInfo { nX = 0, nY = 0, nWidth = 640, nHeight = 480 }, cancellationToken);
await controlCenter.Detect.ClearDetectResultAsync(camera.NativeCameraId, cancellationToken);
await camera.StopRecordingAsync(cancellationToken);
```

这些方法包装同步原生调用，取消令牌在进入 SDK 前检查，不能中断执行中的 SDK 调用。

## PLC TCP 通信

`Vehicle:PLC` 配置地址和端口，当前为 Demo 中的 `192.168.7.45:8080`。启动项目即尝试连接；`ConnectTimeoutSeconds` 默认 5 秒，连接失败或断开后每隔 `ReconnectIntervalSeconds`（默认 5 秒）重试。暂时不需要 PLC 时，将 `Enabled` 设为 `false`。

连接与接收任务由控制中心持有并在停止时取消、等待退出；不另外创建无人等待的接收任务。日志会显示连接状态及收到的十六进制字节。当前没有应用层心跳，突然断网不一定立即被发现。

业务层通过注入的单例 `PLCManager` 调用 `await plcManager.SendDataAsync(commandBytes, cancellationToken)` 发送原始字节，不使用 `new PLCManager()` 或手动 Dispose。并发发送会串行写入，未连接时抛异常，发送失败不自动重发，避免重复执行设备指令。写入完成仅表示数据交给 TCP，不表示 PLC 已执行成功。

启动时不会自动发送 `HelloWorld`。当前只实现 TCP 传输，尚未实现 PLC 业务报文、应答匹配或控制指令；TCP 一次读取可能是半包或多个报文，不能直接视为一条完整消息。后续按协议增加缓存拆包和业务处理。

## 雪花 ID

数据库主键及逻辑关联字段保持 `long`。新 ID 使用秒级时间差、5 位数据中心、5 位设备编号和 10 位序号：

```text
ID = (距 Epoch 的秒数 << 20) | (DataCenterId << 15) | (WorkerId << 10) | 序号
```

- 使用配置中的 `2026-01-01T00:00:00Z` 起始时间，2026 年 9 月生成的 ID 为 14 位；位数随时间增长，不保证永久 14 位。
- 生成器限制结果不超过 JavaScript 最大安全整数 `9007199254740991`，超出范围会报错，不会截断或取模。
- 最多支持 32 个数据中心，每个中心 32 个节点；每节点每秒最多生成 1024 个 ID，超出时等待下一秒。
- 为避免正常重启重用序号，生成器跳过启动所在秒，首次调用最多等待约 1 秒。每个节点只能运行一个进程，运行期间时钟回拨会报错；跨重启也应保证时钟不回拨。
- 部署后不要随意修改 Epoch 或重复分配节点编号。
- 本次仅改变新 ID 的生成方式，不重编号已有数据。旧的毫秒级长 ID 及其关联仍然保留，仍存在浏览器数字精度风险；新旧 ID 混用时不要按 ID 判断创建时间，应按 `CreateTime` 排序。
