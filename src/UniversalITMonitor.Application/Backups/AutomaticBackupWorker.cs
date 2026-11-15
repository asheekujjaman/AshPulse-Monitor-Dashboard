using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace UniversalITMonitor.Application.Backups;

public sealed class AutomaticBackupWorker(
    IBackupService backups,
    ILogger<AutomaticBackupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await backups.CreateDueAutomaticBackupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning("Automatic backup check failed ({FailureType}).", exception.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
