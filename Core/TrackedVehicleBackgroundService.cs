namespace TrackedVehicle.Core;

public sealed class TrackedVehicleBackgroundService(
    ILogger<TrackedVehicleBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan ExecutionInterval = TimeSpan.FromSeconds(5);

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("巡检小车后台服务正在启动");
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(ExecutionInterval);

        do
        {
            logger.LogInformation("巡检小车后台服务周期运行，时间：{Time}", DateTimeOffset.Now);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("巡检小车后台服务正在停止");
        await base.StopAsync(cancellationToken);
    }
}
