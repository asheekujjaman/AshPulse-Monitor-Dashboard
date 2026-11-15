using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Notifications;

public sealed class NotificationDeliveryWorker(
    INotificationService notifications,
    ILogger<NotificationDeliveryWorker> logger,
    IApplicationMaintenanceLock? maintenanceLock = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var maintenance = maintenanceLock is null
                    ? null
                    : await maintenanceLock.EnterReadAsync(stoppingToken);
                await notifications.ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError("Notification queue processing failed ({FailureType}). Pending notifications will be retried.", exception.GetType().Name);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
