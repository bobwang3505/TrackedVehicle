using TrackedVehicle.NativeInterop;

namespace TrackedVehicle.Core.Impl;

/// <summary>管理 SDK 全局模型，并串行执行模型初始化、方向定位检测及结果清理。</summary>
/// <remarks>内部同步调用 SDK；检测期间需保持相机打开。</remarks>
public sealed class DetectManager(ILogger<DetectManager> logger) : IDetectManager
{
    // 所有相机共享这个检测管理实例的锁，避免模型初始化、检测和清理同时调用 SDK。
    private readonly object _sync = new();
    // 只有本实例最近一次模型初始化成功后才允许检测。
    private bool _initialized;

    /// <inheritdoc />
    public Task InitModelAsync(string modelPath, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
            if (modelPath.Contains('\0'))
                throw new ArgumentException("模型路径不能包含空字符。", nameof(modelPath));

            // 重新初始化前先清除成功标记，若 SDK 失败，后续检测不能沿用旧的成功状态。
            _initialized = false;
            // 相对路径以程序所在目录为基准，避免从其他目录启动时找不到随程序发布的模型。
            // C++ 接口要求模型所在目录，不带模型文件名；也可配置开发板上的绝对目录。
            modelPath = Path.GetFullPath(modelPath, AppContext.BaseDirectory);
            logger.LogInformation("准备初始化检测模型，传给 RobotX_InitModel 的完整目录：{ModelPath}，目录存在：{Exists}",
                modelPath, Directory.Exists(modelPath));
            if (!Directory.Exists(modelPath))
                throw new DirectoryNotFoundException($"检测模型目录不存在：{modelPath}");
            CheckResult(NativeMethods.RobotX_InitModel(modelPath), "初始化模型");
            _initialized = true;
            logger.LogInformation("检测模型初始化成功");
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<OrientationPosInfo> OrientationPosDetectAsync(int camId, RoiInfo roi, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentOutOfRangeException.ThrowIfNegative(camId);
            if (!_initialized)
                throw new InvalidOperationException("请先初始化检测模型。");
            if (roi.nX < 0 || roi.nY < 0 || roi.nWidth <= 0 || roi.nHeight <= 0)
                throw new ArgumentException("检测区域坐标不能为负数，宽高必须大于 0。", nameof(roi));

            // 新版 SDK 只输出 centerX；未约定坐标基准及无目标标记，不自行换算或判断有效性。
            var result = new OrientationPosInfo();
            CheckResult(NativeMethods.RobotX_OrientationPosDetect(camId, in roi, ref result), "方向定位检测");
            return Task.FromResult(result);
        }
    }

    /// <inheritdoc />
    public Task ClearDetectResultAsync(int camId, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentOutOfRangeException.ThrowIfNegative(camId);
            CheckResult(NativeMethods.RobotX_ClearDetectResultInfo(camId), "清除检测结果");
        }
        return Task.CompletedTask;
    }

    /// <summary>SDK 返回非 0 时抛出异常。</summary>
    private static void CheckResult(int result, string operation)
    {
        if (result != 0)
            throw new InvalidOperationException($"{operation}失败，SDK 返回码：{result}。");
    }
}
