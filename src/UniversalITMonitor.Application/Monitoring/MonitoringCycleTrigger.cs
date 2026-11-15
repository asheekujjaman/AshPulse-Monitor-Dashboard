namespace UniversalITMonitor.Application.Monitoring;

public sealed class MonitoringCycleTrigger : IMonitoringCycleTrigger, IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void RequestImmediateCycle()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Multiple changes during a current cycle collapse into one immediate follow-up.
            }
        }
    }

    public Task<bool> WaitForNextCycleAsync(TimeSpan interval, CancellationToken cancellationToken) =>
        _signal.WaitAsync(interval, cancellationToken);

    public void Dispose() => _signal.Dispose();
}
