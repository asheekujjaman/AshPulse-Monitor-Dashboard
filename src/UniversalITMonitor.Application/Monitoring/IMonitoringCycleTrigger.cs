namespace UniversalITMonitor.Application.Monitoring;

public interface IMonitoringCycleTrigger
{
    void RequestImmediateCycle();

    Task<bool> WaitForNextCycleAsync(TimeSpan interval, CancellationToken cancellationToken);
}
