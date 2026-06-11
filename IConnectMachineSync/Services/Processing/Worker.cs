using IConnectMachineSync.Configuration;
using IConnectMachineSync.Services.Interface;
using Microsoft.Extensions.Options;

namespace IConnectMachineSync.Services.Processing;

public sealed class Worker(IJob job, IOptions<AppOptions> options, ILogger<Worker> logger) : BackgroundService
{
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(Math.Max(10, options.Value.Sync.IntervalSeconds));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("IConnect machine sync worker started. Interval: {Interval}.", _interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            var startedAt = DateTimeOffset.Now;
            try
            {
                await job.ExecuteAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Machine sync cycle failed.");
            }

            var remaining = _interval - (DateTimeOffset.Now - startedAt);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, stoppingToken);
        }
    }
}
