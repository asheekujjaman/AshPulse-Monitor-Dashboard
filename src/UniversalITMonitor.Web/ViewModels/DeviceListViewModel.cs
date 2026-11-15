using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Application.Inventory;

namespace UniversalITMonitor.Web.ViewModels;

public sealed class DeviceListViewModel
{
    public IReadOnlyList<DeviceInfo> Devices { get; init; } = [];

    public string? Search { get; init; }

    public DeviceStatus? Status { get; init; }

    public DeviceType? Type { get; init; }

    public string? Location { get; init; }

    public string? Group { get; init; }

    public bool? MonitoringEnabled { get; init; }

    public string? Sort { get; init; }

    public int TotalCount { get; init; }

    public int UpCount { get; init; }

    public int DownCount { get; init; }

    public int UnknownCount { get; init; }

    public IReadOnlyList<string> Locations { get; init; } = [];

    public IReadOnlyList<string> Groups { get; init; } = [];
}

public sealed class DeviceImportViewModel
{
    public int Imported { get; set; }

    public IReadOnlyList<string> Errors { get; set; } = [];

    public string? Message { get; set; }
}

public sealed class DashboardViewModel
{
    public IReadOnlyList<DeviceInfo> Devices { get; init; } = [];

    public IReadOnlyList<DeviceInfo> Servers { get; init; } = [];

    public IReadOnlyList<UniversalITMonitor.Core.Models.NotificationRecord> RecentAlertTransitions { get; init; } = [];

    public IReadOnlyList<DashboardAvailabilityPoint> AvailabilityHistory { get; init; } = [];

    public IReadOnlyList<DashboardResponsePoint> ResponseHistory { get; init; } = [];

    public int TotalCount { get; init; }

    public int UpCount { get; init; }

    public int DownCount { get; init; }

    public int UnknownCount { get; init; }

    public int MonitoringEnabledCount { get; init; }

    public int PollingIntervalSeconds { get; init; }

    public IReadOnlyList<LocationSummary> Locations { get; init; } = [];

    public IReadOnlyList<GroupSummary> Groups { get; init; } = [];

    public int IpamAddressCount { get; init; }

    public int IpamAssignedCount { get; init; }

    public int IpamAvailableCount { get; init; }

    public int IpamReservedCount { get; init; }

    public int IpamConflictCount { get; init; }

    public int RecentNotificationEventCount { get; init; }

    public int RecentNotificationFailureCount { get; init; }

    public double? CurrentAvailabilityPercent { get; init; }

    public double? AvailabilityCoveragePercent { get; init; }

    public int DevicesWithAvailabilityHistory { get; init; }

    public long? AverageResponseTimeMilliseconds { get; init; }

    public int DevicesWithCurrentResponse { get; init; }

    public int ResponseSampleCount { get; init; }

    public double UpPercent { get; init; }

    public double DownPercent { get; init; }

    public double UnknownPercent { get; init; }
}

public sealed record DashboardAvailabilityPoint(
    string Label,
    DateTimeOffset PeriodStartUtc,
    double? AvailabilityPercent,
    double CoveragePercent);

public sealed record DashboardResponsePoint(
    string Label,
    DateTimeOffset PeriodStartUtc,
    long? AverageResponseMilliseconds,
    int SampleCount,
    double HeightPercent);
