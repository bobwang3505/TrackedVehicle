namespace TrackedVehicle.Core;

public sealed class TrackedVehicleBackgroundService(
    VehicleControlCenter controlCenter,
    ILogger<TrackedVehicleBackgroundService> logger) : BackgroundService
{

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("巡检小车后台服务正在启动");
        await base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return controlCenter.RunAsync(stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("巡检小车后台服务正在停止");
        await base.StopAsync(cancellationToken);
    }
}
