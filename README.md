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

### CameraManager 中的 `_sync` 和 `lock` 是什么？

可以把 `_sync` 理解成每个相机自己的一把“操作锁”：

```csharp
private readonly object _sync = new();

lock (_sync)
{
    // 持有这把锁的线程才能执行；其他线程遇到同一把锁时需要等待。
}
```

`readonly` 保证锁对象不会被替换。打开、关闭、开始录像、停止录像使用同一个 `_sync`，让同一个相机的这些操作互斥执行，避免同时修改 `_camId`、`_recordId` 或冲突调用 SDK。

下面的 A、B 代表两个不同的线程，不是两台相机。它们同时调用同一个 `CameraManager` 实例（例如相机 1）的 `OpenAsync()`，因此竞争的是这个实例的同一个 `_sync`：

1. 不加锁时，线程 A、线程 B 可能都看到“相机 1 未打开”，随后各调用一次 SDK，造成重复打开。
2. 加锁后，假设线程 A 先拿到相机 1 的锁，线程 B 就等待；线程 A 完成打开并保存 `_camId` 后释放锁。
3. 线程 B 拿到相机 1 的锁，发现 `_camId` 已有值，直接返回，不再重复打开。

这里的 `lock` 可以理解为线程锁，更准确地说是同一进程内的线程互斥锁，属于悲观锁：先获得锁，再执行受保护的操作；其他线程需要等待持锁线程释放锁。锁关联的是 `_sync` 对象，不会自动锁住整个相机对象，也不会自动保护未加锁的方法。

乐观并发控制则通常不先加互斥锁，而是在更新时通过版本号或比较并交换等方式检查是否发生冲突，失败后重试或报错。本例采用“先拿锁再操作”的悲观方式，避免重复打开、关闭或录像这些不能随意重复执行的 SDK 操作。

需要记住的几点：

- 五个 `CameraManager` 有五把独立的锁。若线程 A 操作相机 1、线程 B 操作相机 2，它们使用不同的锁，不会因为 `_sync` 互相等待。
- 正常执行结束、`return` 或抛异常都会自动释放锁，无需手动解锁。
- 同一线程可以再次进入同一把锁，因此 `StartRecordingAsync()` 在锁内读取同样加锁的 `NativeCameraId` 是允许的。
- 锁只保护使用同一把锁的代码。`NativeCameraId` 返回后锁就释放了，调用方仍需保证后续检测期间相机不被关闭。
- `lock` 等待时会阻塞当前线程，不是异步等待；锁内不能使用 `await`。这里的方法虽然返回 `Task`，锁内实际执行的是同步 SDK 调用。
- SDK 回调当前不获取 `_sync`：如果 SDK 持锁调用期间等待另一个线程完成回调，而回调又等待这把锁，就可能互相等待形成死锁。

## PLC TCP 通信

`Vehicle:PLC` 配置地址和端口，当前为 Demo 中的 `192.168.7.45:8080`。启动项目即尝试连接；`ConnectTimeoutSeconds` 默认 5 秒，连接失败或断开后每隔 `ReconnectIntervalSeconds`（默认 5 秒）重试。暂时不需要 PLC 时，将 `Enabled` 设为 `false`。

连接与接收任务由控制中心持有并在停止时取消、等待退出；不另外创建无人等待的接收任务。日志会显示连接状态及收到的十六进制字节。当前没有应用层心跳，突然断网不一定立即被发现。

业务层通过注入的单例 `PLCManager` 调用 `await plcManager.SendDataAsync(commandBytes, cancellationToken)` 发送原始字节，不使用 `new PLCManager()` 或手动 Dispose。并发发送会串行写入，未连接时抛异常，发送失败不自动重发，避免重复执行设备指令。写入完成仅表示数据交给 TCP，不表示 PLC 已执行成功。

启动时不会自动发送 `HelloWorld`。当前只实现 TCP 传输，尚未实现 PLC 业务报文、应答匹配或控制指令；TCP 一次读取可能是半包或多个报文，不能直接视为一条完整消息。后续按协议增加缓存拆包和业务处理。

## 数据库表结构更新（SqlSugar CodeFirst）

项目使用 SQLite 和 SqlSugar CodeFirst。`Program.cs` 在每次启动时调用 `app.InitializeDatabase()`，由 `Extensions/DatabaseInitializerExtensions.cs` 执行：

```csharp
database.DbMaintenance.CreateDatabase();
database.CodeFirst.InitTables<InspectionRecord, InspectionVideoFile>();
```

数据库或表不存在时创建；表已存在时，根据实体补充缺少的字段。修改实体后需要重新编译并启动程序，只有编译不会更新数据库；部署到设备时，需要部署新版本并重启设备上的程序。

更新步骤：

1. 在实体中新增属性，配置好 `SugarColumn` 的可空性、默认值等。新增实体表时，还需要将实体加入 `InitTables` 的初始化列表。
2. 重新编译并启动程序，启动时执行表结构同步。
3. 查看启动时输出的 SqlSugar SQL，并在数据库工具中刷新表结构，确认字段已经添加。

例如文件实体新增 `S3Key` 和 `IsUploaded` 后，启动时会给 `inspection_video_files` 表添加相应列：`S3Key` 允许为空，已有记录初始为 `NULL`；`IsUploaded` 配置了 `DefaultValue = "0"`，已有记录初始为 `false`。

数据库文件由 `Database:SQLitePath` 配置，当前为 `Data/tracked_vehicle.db`；相对路径按程序的内容根目录解析。检查更新结果时，应打开运行实例实际使用的数据库文件。

当前 SQLite CodeFirst 配置用于新增表和字段，不会因实体删除属性就自动删除数据库中的旧列。例如之前已经建过 `IsRecordingCompleted` 列，删除实体属性后，数据库可能仍保留该列。字段删除、改名、修改类型或约束需单独处理数据库迁移，不能直接按新增字段的方式理解。表结构同步也不会自动完成业务数据转换或回填；本次设置默认值只表示已有文件默认为未上传。

## 巡检记录与 RustFS 视频上传

开始、结束和录像完成的业务触发时机暂未接入 PLC 或相机 SDK；以下入口已经可以调用：

| 时机 | 调用入口 | 行为 |
| --- | --- | --- |
| 确认开始巡检 | `IInspectionService.StartAsync(startTime)` | 新建巡检记录，返回巡检 ID；调用方随后启动录像，并把 ID 与该次录像绑定 |
| 确认结束巡检 | `IInspectionService.FinishAsync(inspectionId, endTime)` | 更新结束时间，重复调用保留首次结束时间；调用方负责停止对应录像 |
| 已完成切片，处于普通异步业务流程 | `IInspectionService.AddVideoFileAsync(inspectionId, request)` | 等待文件入库，后续由后台定期扫描上传 |
| 已完成切片，处于原生回调 | `RecordingFileRegistrationService.TryNotifyCompleted(inspectionId, fullPath, cameraId, startTime, endTime)` | 只提交完成通知，后台串行存库，上传由定期扫描负责，不在回调中等待数据库或网络 |

巡检结束时间按业务结束事件记录，不等待上传。停止录像产生的最后一个切片仍可登记到已结束的巡检中。`TryNotifyCompleted` 必须传该次录像所属的巡检 ID 和已经写完的文件完整路径，不能读取可能已经切换的“当前巡检 ID”。已约定巡检 ID 使用开始巡检时新建记录的 ID，回调文件名为完整路径；回调时间暂按 Unix 毫秒时间戳转换为本地 DateTime。当前 CameraManager.OnMediaFinish 已转换时间并记录日志，尚未绑定巡检 ID 或调用 TryNotifyCompleted；SDK 时间格式及最后一个切片的回调时序需在设备联调时核对。

完成通知入口返回 `true` 只表示进入内存队列，不代表已存库。队列满、停止或参数无效时返回 `false`，未来接入回调时必须保留并补交通知。后台遇到存库异常每 5 秒重试当前通知；同一巡检、同一相机、同一路径的重复通知复用已有文件记录，每个切片必须使用唯一且不被覆盖的路径。正常停机先停止相机，再尽量排空完成通知，最后停止上传。异常断电可能丢失尚未入库的内存通知，这部分无法靠数据库扫描恢复，需依据本地文件和所属巡检补登记；已入库文件则可重启恢复上传。

文件表新增 CameraId、StartTime、EndTime：分别保存配置中的相机字符串业务 Id 和切片实际录像起止时间。旧记录缺失的信息允许为空，不以入库时间补填；新登记请求必须提供这三项，结束时间不能早于开始时间。重新编译并启动后由 CodeFirst 添加字段。后续回放按巡检 ID 和 CameraId 筛选，再按 StartTime 排序；CreateTime 仍表示入库记录创建时间。SDK 回调时间暂用 DateTimeOffset.FromUnixTimeMilliseconds(value).LocalDateTime 转换，后续接入完成通知时传转换后的值；以部署设备的本地时区为准，联调发现时间格式不符时再调整。

上传流程：

1. 新切片录完后存库，IsUploaded 为 false；不再维护上传队列和内存去重字典。录像完成通知到后台存库的队列仍然保留。
2. 启用上传后立即扫描数据库，按 ID 从小到大分批读取未删除、未上传记录，也兼容旧数据的 IsUploaded 为 NULL 或空字符串。
3. 每批最多查询 MaxConcurrency 个文件并启动上传任务，等待本批全部处理结束才查询下一批。上传前再次检查记录状态；同一实例的各批、各轮不会重叠。失败记录本轮不反复处理，也不会阻挡后续 ID 的文件。
4. 默认对象 key 为 `inspections/{巡检ID}/{文件ID}{扩展名}`；已有 S3Key 时复用。上传成功后用一条 UPDATE 同时写入 S3Key 和 IsUploaded = true；失败保持未上传状态，下轮重试。本地文件不会自动删除。相同 key 的重试在开启版本管理的 bucket 中仍可能产生多个版本。
5. 一轮扫描及上传结束后，等待 ScanIntervalSeconds 秒（默认 60），再从 ID 0 开始新一轮。存库不会立即触发上传；程序重启后会重新扫描未上传记录。

`VideoUpload` 配置修改后重启生效：

| 配置 | 说明 |
| --- | --- |
| `Enabled` | 默认 `false`，关闭时仍可登记文件，但不扫描或上传 |
| `ServiceUrl` | RustFS 的 S3 API 地址，例如 `http://服务器地址:9000`，不是管理控制台地址 |
| `Region` | 默认 `us-east-1`，按服务端实际配置填写 |
| `BucketName` | 已创建的目标 bucket，本程序不自动建 bucket |
| `AccessKey` / `SecretKey` | 访问凭据；可通过环境变量 `VideoUpload__AccessKey` / `VideoUpload__SecretKey` 提供，勿将真实凭据提交到仓库 |
| `MaxConcurrency` | 同时上传的文件数，默认 2，范围 1-32 |
| `QueueCapacity` | 仅用于录像完成后等待存库的通知队列，默认 100；上传不使用此配置 |
| `ScanIntervalSeconds` | 一轮扫描及上传完成后等待的秒数，默认 60；上传失败由后续扫描重试 |

采用 AWS SDK 的 S3 客户端连接 RustFS，并启用 path-style 地址。参考 [RustFS 官方 SDK 接入说明](https://github.com/rustfs/docs.rustfs.com/blob/main/content/en/developer/sdk/javascript.md) 和 [AWS .NET TransferUtility 上传接口](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/S3/MTransferUtilityUploadAsyncStringStringStringCancellationToken.html)。当前通过单实例串行扫描批次避免重复调度，同一数据库应由一个实例负责上传；未进行 RustFS 和真实录像设备联调。

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
