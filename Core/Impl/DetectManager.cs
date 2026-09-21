namespace TrackedVehicle.Core.Impl;

/// <summary>管理 SDK 全局模型，并串行执行模型初始化、轨道检测及结果清理。</summary>
public sealed class DetectManager(ILogger<DetectManager> logger) : IDetectManager
{
    private readonly object _sync = new();
    private bool _initialized;

    public Task InitModelAsync(string modelPath, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
            if (modelPath.Contains('\0'))
                throw new ArgumentException("模型路径不能包含空字符。", nameof(modelPath));

            _initialized = false;
            CheckResult(NativeMethods.RobotX_InitModel(modelPath), "初始化模型");
            _initialized = true;
            logger.LogInformation("检测模型初始化成功");
        }
        return Task.CompletedTask;
    }

    public Task<LaneDetectGroup> LaneDetectAsync(int camId, RoiInfo roi, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentOutOfRangeException.ThrowIfNegative(camId);
            if (!_initialized)
                throw new InvalidOperationException("请先初始化检测模型。");
            if (roi.nX < 0 || roi.nY < 0 || roi.nWidth <= 0 || roi.nHeight <= 0)
                throw new ArgumentException("检测区域坐标不能为负数，宽高必须大于 0。", nameof(roi));

            var group = new LaneDetectGroup { infos = new LaneDetectInfo[NativeMethods.MAX_COUNT] };
            CheckResult(NativeMethods.RobotX_LaneDetect(camId, in roi, ref group), "轨道检测");
            if (group.count < 0 || group.count > NativeMethods.MAX_COUNT)
                throw new InvalidOperationException($"SDK 返回的检测结果数量无效：{group.count}。");
            return Task.FromResult(group);
        }
    }

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

    private static void CheckResult(int result, string operation)
    {
        if (result != 0)
            throw new InvalidOperationException($"{operation}失败，SDK 返回码：{result}。");
    }
}
