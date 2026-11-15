using System.ComponentModel.DataAnnotations;
using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class MonitoringSettingsViewModel
{
    [Range(10, 3600)]
    [Display(Name = "Polling interval (seconds)")]
    public int PollingIntervalSeconds { get; set; } = 60;

    [Range(1, 64)]
    [Display(Name = "Maximum concurrent checks")]
    public int MaximumConcurrentChecks { get; set; } = 20;

    [Range(250, 10000)]
    [Display(Name = "Ping timeout (milliseconds)")]
    public int PingTimeoutMilliseconds { get; set; } = 1500;

    public static MonitoringSettingsViewModel FromSettings(ApplicationSettings settings) => new()
    {
        PollingIntervalSeconds = settings.PollingIntervalSeconds,
        MaximumConcurrentChecks = settings.MaximumConcurrentChecks,
        PingTimeoutMilliseconds = settings.PingTimeoutMilliseconds
    };

    public ApplicationSettings ToSettings() => new()
    {
        PollingIntervalSeconds = PollingIntervalSeconds,
        MaximumConcurrentChecks = MaximumConcurrentChecks,
        PingTimeoutMilliseconds = PingTimeoutMilliseconds
    };
}
