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

配置里的相机 IP 和账号仍为示例，实际运行前需要补全。CameraIP 支持完整 RTSP 地址（包含码流路径及认证信息），或使用纯 IP、Port、UserName、Pwd 生成 rtsp://账号:密码@IP:端口/，账号密码进行 URL 转义。地址和别名不能含空字符，且最多 199 个 UTF-8 字节。打开失败会退出控制中心并清理已打开相机；关闭失败记录错误并继续清理其他相机，目前不自动重试。旧的原生 SDK 测试 Controller 已移除，相机启停由控制中心调度；底层声明集中在 `NativeInterop/NativeMethods.cs`，业务直接调用 `NativeMethods.RobotX_*`；结构体、回调及调用示例见 `NativeInterop/README.md`。

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

使用 [Yitter.IdGenerator](https://github.com/yitter/IdGenerator) 1.0.14 的雪花漂移算法（Method = 1）。参考 [ABP 的生成器接口设计](https://abp.io/docs/10.4/framework/infrastructure/guid-generation)，将 ID 生成作为独立的基础设施服务，集中放在 `Infrastructure/IdGeneration`：

- `IIdGenerator`：提供 `long Create()`，供数据库或业务服务注入使用。
- `SnowflakeIdGenerator`：内部实现，持有一个 Yitter `DefaultIdGenerator`。
- `SnowflakeOptions`：雪花算法配置，与实现放在同一功能目录。

启动时先调用 `AddSnowflakeIdGeneration(configuration)` 绑定配置并注册单例，再调用 `AddSqlSugarSqlite(...)`。SqlSugar 只依赖 `IIdGenerator` 接口，生成器也可独立于数据库使用。

SqlSugar 插入对象时，在 `Id` 为 0（未赋值）时自动生成并回填主键。巡检记录和视频文件均适用，业务层继续使用 `Insertable(entity).ExecuteCommandAsync()`；已赋值的 ID 和更新操作不会重新生成 ID。数据库主键及逻辑关联字段保持 `long`。

- `Snowflake:WorkerId`：范围 0-63，默认配置为 1。每个运行实例必须分配不同编号；原 `DataCenterId` 配置已移除，设备编号需全局唯一。
- `Snowflake:Epoch`：映射到 Yitter 的 `BaseTime`，当前为 `2026-01-01T00:00:00Z`，部署后不要随意修改。
- 固定使用 6 位设备编号和 6 位序号，时间戳为毫秒。ID 长度随时间增长，不保证固定 14 位，也不永久保证 JavaScript 安全整数范围；前端处理超过安全范围的历史或未来 ID 时应使用字符串传输方案。
- 单例仅在本进程内保证共享生成器，不能让多个进程共用同一 WorkerId。重启时应确保系统时间超过上次生成 ID 的时间（含漂移），不要手动大幅回拨系统时钟。
- 切换算法不修改已有数据及关联。新旧 ID 不适合用于推断创建先后，排序请使用 `CreateTime`。
