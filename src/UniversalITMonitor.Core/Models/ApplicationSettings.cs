namespace UniversalITMonitor.Core.Models;

public sealed class ApplicationSettings
{
    public int PollingIntervalSeconds { get; set; } = 60;

    public int MaximumConcurrentChecks { get; set; } = 20;

    public int PingTimeoutMilliseconds { get; set; } = 1500;
}
