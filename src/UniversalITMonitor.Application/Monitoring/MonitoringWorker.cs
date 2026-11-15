using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Notifications;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Monitoring;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Monitoring;

public sealed class MonitoringWorker(
    IDeviceService devices,
    IDeviceRepository repository,
    IMonitoringHistoryStore history,
    ISnmpMetricsStore metricsStore,
    IMonitoringSampleStore samples,
    INotificationService notifications,
    IJsonDocumentStore documents,
    IIcmpProbe icmp,
    ISnmpMetricsCollector snmp,
    IMonitoringCycleTrigger cycleTrigger,
    ILogger<MonitoringWorker> logger,
    IApplicationMaintenanceLock? maintenanceLock = null) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = new ApplicationSettings();
            try
            {
                await using var maintenance = maintenanceLock is null
                    ? null
                    : await maintenanceLock.EnterReadAsync(stoppingToken);
                settings = await ReadSettingsAsync(stoppingToken);
                await RunCycleAsync(settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Monitoring cycle failed. The next cycle will continue after the configured interval.");
            }

            try
            {
                await cycleTrigger.WaitForNextCycleAsync(TimeSpan.FromSeconds(settings.PollingIntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<ApplicationSettings> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var stored = await documents.ReadAsync("settings.json", static () => new ApplicationSettings(), cancellationToken);
            return new ApplicationSettings
            {
                PollingIntervalSeconds = Math.Clamp(stored.PollingIntervalSeconds, 10, 3600),
                MaximumConcurrentChecks = Math.Clamp(stored.MaximumConcurrentChecks, 1, 64),
                PingTimeoutMilliseconds = Math.Clamp(stored.PingTimeoutMilliseconds, 250, 10000)
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not read monitoring settings; using the built-in polling defaults.");
            return new ApplicationSettings();
        }
    }

    private async Task RunCycleAsync(ApplicationSettings settings, CancellationToken cancellationToken)
    {
        await DrainPendingTransitionsAsync(cancellationToken);

        var monitoredDevices = await devices.GetForMonitoringAsync(cancellationToken);
        if (monitoredDevices.Count == 0)
        {
            return;
        }

        var results = new ConcurrentBag<DeviceCheckResult>();
        await Parallel.ForEachAsync(
            monitoredDevices,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = settings.MaximumConcurrentChecks,
                CancellationToken = cancellationToken
            },
            async (device, token) =>
            {
                var checkedUtc = DateTimeOffset.UtcNow;
                IcmpProbeResult pingResult;
                try
                {
                    pingResult = await icmp.CheckAsync(device.IpAddress, settings.PingTimeoutMilliseconds, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    pingResult = new IcmpProbeResult(false, null, SafeError(exception));
                }

                SnmpMetrics? metrics = null;
                if (device.MonitoringMethod == MonitoringMethod.IcmpAndSnmp)
                {
                    try
                    {
                        metrics = await snmp.CollectAsync(device, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        metrics = new SnmpMetrics
                        {
                            CollectedUtc = DateTimeOffset.UtcNow,
                            CollectionError = SafeError(exception)
                        };
                    }
                }

                results.Add(new DeviceCheckResult
                {
                    DeviceId = device.Id,
                    IpAddress = device.IpAddress,
                    CheckedUtc = checkedUtc,
                    Status = pingResult.IsReachable ? DeviceStatus.Up : DeviceStatus.Down,
                    ResponseTimeMilliseconds = pingResult.ResponseTimeMilliseconds,
                    Error = pingResult.IsReachable ? null : pingResult.Error,
                    SnmpMetrics = metrics
                });
            });

        var batchResult = await repository.RecordChecksAsync(results.ToArray(), cancellationToken);
        var appliedIds = batchResult.AppliedDeviceIds.ToHashSet();
        await Parallel.ForEachAsync(
            results.Where(result => appliedIds.Contains(result.DeviceId)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = settings.MaximumConcurrentChecks,
                CancellationToken = cancellationToken
            },
            async (result, token) =>
            {
                try
                {
                    await samples.AppendAsync(new MonitoringCheckSample
                    {
                        DeviceId = result.DeviceId,
                        CheckedUtc = result.CheckedUtc,
                        Status = result.Status,
                        ResponseTimeMilliseconds = result.Status == DeviceStatus.Up ? result.ResponseTimeMilliseconds : null,
                        Error = result.Error
                    }, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Failed to persist monitoring sample for device {DeviceId}.", result.DeviceId);
                }
            });

        await Parallel.ForEachAsync(
            results.Where(result => result.SnmpMetrics is not null && appliedIds.Contains(result.DeviceId)),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(settings.MaximumConcurrentChecks, 16),
                CancellationToken = cancellationToken
            },
            async (result, token) =>
            {
                try
                {
                    await metricsStore.WriteAsync(result.DeviceId, result.SnmpMetrics!, token);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Failed to persist SNMP metrics for device {DeviceId}.", result.DeviceId);
                }
            });

        await DrainPendingTransitionsAsync(cancellationToken);
    }

    private async Task DrainPendingTransitionsAsync(CancellationToken cancellationToken)
    {
        var pending = await repository.GetPendingTransitionsAsync(cancellationToken);
        foreach (var transition in pending)
        {
            var persisted = true;
            try
            {
                await history.AppendAsync(transition, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to append status history for device {DeviceId}.", transition.DeviceId);
                persisted = false;
            }

            try
            {
                await notifications.QueueTransitionAsync(transition, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to queue notification for device {DeviceId}; monitoring will continue.", transition.DeviceId);
                persisted = false;
            }

            if (persisted)
            {
                try
                {
                    await repository.CompleteTransitionAsync(transition.Id, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Could not acknowledge persisted transition {TransitionId}; it will be replayed safely.", transition.Id);
                }
            }
        }
    }

    private static string SafeError(Exception exception) =>
        exception is System.Net.NetworkInformation.PingException or TimeoutException
            ? "The device did not respond before the check timed out."
            : exception.Message.Length <= 256 ? exception.Message : exception.Message[..256];
}
