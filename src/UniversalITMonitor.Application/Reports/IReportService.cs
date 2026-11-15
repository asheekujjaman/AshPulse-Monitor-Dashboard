using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Core.Models;

namespace UniversalITMonitor.Application.Reports;

public interface IReportService
{
    Task<MonitoringReportData> GetAsync(MonitoringReportQuery query, CancellationToken cancellationToken = default);

    Task<ReportExport> ExportCsvAsync(MonitoringReportQuery query, ReportExportKind kind, CancellationToken cancellationToken = default);
}

public sealed record MonitoringReportQuery(
    DateOnly StartDate,
    DateOnly EndDate,
    Guid? DeviceId = null,
    Guid? LocationId = null,
    Guid? GroupId = null,
    DeviceStatus? Status = null,
    IpAssignmentStatus? IpAssignmentStatus = null);

public sealed record DeviceAvailabilityReport(
    DeviceInfo Device,
    TimeSpan Up,
    TimeSpan Down,
    TimeSpan Unknown,
    double? AvailabilityPercent,
    double CoveragePercent,
    string StatusHistoryNote,
    int ResponseSampleCount,
    long? MinimumResponseMilliseconds,
    long? AverageResponseMilliseconds,
    long? MaximumResponseMilliseconds,
    string ResponseHistoryNote);

public sealed record ReportTransitionRow(
    string DeviceName,
    string IpAddress,
    string? Location,
    string? Group,
    MonitoringHistoryEntry Transition);

public sealed record ReportResponseSampleRow(
    string DeviceName,
    string IpAddress,
    string? Location,
    MonitoringCheckSample Sample);

public sealed record ReportStatusSummary(
    string Name,
    string Path,
    int DeviceCount,
    int UpCount,
    int DownCount,
    int UnknownCount);

public sealed record MonitoringReportData(
    MonitoringReportQuery Query,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    IReadOnlyList<DeviceAvailabilityReport> Devices,
    IReadOnlyList<ReportTransitionRow> Transitions,
    IReadOnlyList<ReportResponseSampleRow> ResponseSamples,
    IReadOnlyList<ReportStatusSummary> Locations,
    IReadOnlyList<ReportStatusSummary> Groups,
    IReadOnlyList<IpamRecord> IpamRecords,
    IReadOnlyList<DeviceInfo> DeviceChoices,
    IReadOnlyList<ReportChoice> LocationChoices,
    IReadOnlyList<ReportChoice> GroupChoices,
    string? ValidationError);

public sealed record ReportExport(byte[] Content, string FileName);
public sealed record ReportChoice(Guid Id, string Name);

public enum ReportExportKind
{
    Availability,
    Ipam
}

