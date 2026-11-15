using System.Globalization;
using System.Text;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Application.Ipam;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Reports;

public sealed class ReportService(
    IDeviceService devices,
    IInventoryStructureService structureService,
    IIpamService ipam,
    IMonitoringHistoryStore history,
    IMonitoringSampleStore samples) : IReportService
{
    private const int MaximumExportCharacters = 20_000_000;
    public async Task<MonitoringReportData> GetAsync(MonitoringReportQuery query, CancellationToken cancellationToken = default)
    {
        var invalid = Validate(query);
        if (invalid is not null)
        {
            return Empty(query, invalid);
        }

        var (startUtc, endUtc) = GetRange(query);
        var allDevices = await devices.GetAllAsync(cancellationToken);
        var hierarchy = await structureService.GetStructureAsync(cancellationToken);
        var locationChoices = hierarchy.Locations
            .OrderBy(item => InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id), StringComparer.OrdinalIgnoreCase)
            .Select(item => new ReportChoice(item.Id, InventoryStructureService.BuildLocationPath(hierarchy.Locations, item.Id)))
            .ToArray();
        var groupChoices = hierarchy.Groups
            .OrderBy(item => InventoryStructureService.BuildGroupPath(hierarchy.Groups, item.Id), StringComparer.OrdinalIgnoreCase)
            .Select(item => new ReportChoice(item.Id, InventoryStructureService.BuildGroupPath(hierarchy.Groups, item.Id)))
            .ToArray();
        var selectedDeviceIds = allDevices
            .Where(device => !query.DeviceId.HasValue || device.Id == query.DeviceId)
            .Where(device => !query.Status.HasValue || device.Status == query.Status)
            .Where(device => IsInSelectedLocation(device, query.LocationId, hierarchy.Locations))
            .Where(device => IsInSelectedGroup(device, query.GroupId, hierarchy.Groups))
            .Select(device => device.Id)
            .ToHashSet();
        var selectedDevices = allDevices.Where(device => selectedDeviceIds.Contains(device.Id)).ToArray();

        var availability = new List<DeviceAvailabilityReport>(selectedDevices.Length);
        var transitionRows = new List<ReportTransitionRow>();
        var responseRows = new List<ReportResponseSampleRow>();
        foreach (var device in selectedDevices)
        {
            var historyWindow = await history.GetWindowAsync(device.Id, startUtc, endUtc, cancellationToken);
            var sampleWindow = await samples.GetWindowAsync(device.Id, startUtc, endUtc, cancellationToken);
            var totals = CalculateDurations(historyWindow, startUtc, endUtc);
            var responseValues = sampleWindow.Samples
                .Where(sample => sample.Status == DeviceStatus.Up && sample.ResponseTimeMilliseconds.HasValue)
                .Select(sample => sample.ResponseTimeMilliseconds!.Value)
                .ToArray();
            var historyNote = GetHistoryNote(historyWindow, startUtc);
            var responseNote = GetResponseNote(sampleWindow, startUtc);
            availability.Add(new(
                device,
                totals.Up,
                totals.Down,
                totals.Unknown,
                totals.Up + totals.Down == TimeSpan.Zero
                    ? null
                    : totals.Up.TotalSeconds / (totals.Up + totals.Down).TotalSeconds * 100,
                (endUtc - startUtc) <= TimeSpan.Zero
                    ? 0
                    : (totals.Up + totals.Down).TotalSeconds / (endUtc - startUtc).TotalSeconds * 100,
                historyNote,
                responseValues.Length,
                responseValues.Length == 0 ? null : responseValues.Min(),
                responseValues.Length == 0 ? null : (long)Math.Round(responseValues.Average()),
                responseValues.Length == 0 ? null : responseValues.Max(),
                responseNote));

            foreach (var transition in historyWindow.Transitions)
            {
                if (query.Status.HasValue && transition.Status != query.Status.Value) continue;
                transitionRows.Add(new(device.Name, device.IpAddress, device.Location, device.Group, transition));
            }

            foreach (var sample in sampleWindow.Samples)
            {
                if (query.Status.HasValue && sample.Status != query.Status.Value) continue;
                responseRows.Add(new(device.Name, device.IpAddress, device.Location, sample));
            }
        }

        var locations = BuildLocationSummary(selectedDevices, hierarchy.Locations);
        var groups = BuildGroupSummary(selectedDevices, hierarchy.Groups);
        var currentStatusByDevice = allDevices.ToDictionary(device => device.Id, device => device.Status);
        var records = (await ipam.GetRecordsAsync(cancellationToken))
            .Where(record => !query.DeviceId.HasValue || record.DeviceId == query.DeviceId)
            .Where(record => !query.Status.HasValue ||
                record.DeviceId is { } deviceId && currentStatusByDevice.TryGetValue(deviceId, out var deviceStatus) && deviceStatus == query.Status)
            .Where(record => query.IpAssignmentStatus is null || record.AssignmentStatus == query.IpAssignmentStatus)
            .Where(record => IsInSelectedIpamLocation(record, query.LocationId, hierarchy.Locations))
            .Where(record => IsInSelectedIpamGroup(record, query.GroupId, hierarchy.Groups))
            .OrderBy(record => record.IpAddress, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new(
            query,
            startUtc,
            endUtc,
            availability,
            transitionRows.OrderByDescending(row => row.Transition.OccurredUtc).ToArray(),
            responseRows.OrderByDescending(row => row.Sample.CheckedUtc).ToArray(),
            locations,
            groups,
            records,
            allDevices,
            locationChoices,
            groupChoices,
            null);
    }

    public async Task<ReportExport> ExportCsvAsync(
        MonitoringReportQuery query,
        ReportExportKind kind,
        CancellationToken cancellationToken = default)
    {
        var report = await GetAsync(query, cancellationToken);
        if (report.ValidationError is not null) throw new ArgumentException(report.ValidationError, nameof(query));
        var builder = new StringBuilder();
        if (kind == ReportExportKind.Availability)
        {
            builder.AppendLine("RecordType,DeviceName,IpAddress,Location,Group,OccurredUtc,PreviousStatus,Status,ResponseTimeMilliseconds,Details,UpSeconds,DownSeconds,UnknownSeconds,AvailabilityPercent,CoveragePercent,ResponseSampleCount,MinimumResponseMilliseconds,AverageResponseMilliseconds,MaximumResponseMilliseconds,StatusHistory,ResponseHistory");
            foreach (var row in report.Devices)
            {
                builder.AppendLine(string.Join(',',
                    Csv("Summary"), Csv(row.Device.Name), Csv(row.Device.IpAddress), Csv(row.Device.Location), Csv(row.Device.Group),
                    string.Empty, string.Empty, Csv(row.Device.Status.ToString()), string.Empty, string.Empty,
                    Csv(row.Up.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)),
                    Csv(row.Down.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)),
                    Csv(row.Unknown.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)),
                    Csv(row.AvailabilityPercent?.ToString("F2", CultureInfo.InvariantCulture)),
                    Csv(row.CoveragePercent.ToString("F2", CultureInfo.InvariantCulture)),
                    Csv(row.ResponseSampleCount.ToString(CultureInfo.InvariantCulture)),
                    Csv(row.MinimumResponseMilliseconds?.ToString(CultureInfo.InvariantCulture)),
                    Csv(row.AverageResponseMilliseconds?.ToString(CultureInfo.InvariantCulture)),
                    Csv(row.MaximumResponseMilliseconds?.ToString(CultureInfo.InvariantCulture)),
                    Csv(row.StatusHistoryNote), Csv(row.ResponseHistoryNote)));
                EnsureExportSize(builder);
            }

            foreach (var row in report.Transitions)
            {
                builder.AppendLine(string.Join(',',
                    Csv("Transition"), Csv(row.DeviceName), Csv(row.IpAddress), Csv(row.Location), Csv(row.Group),
                    Csv(row.Transition.OccurredUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
                    Csv(row.Transition.PreviousStatus.ToString()), Csv(row.Transition.Status.ToString()),
                    Csv(row.Transition.ResponseTimeMilliseconds?.ToString(CultureInfo.InvariantCulture)), Csv(row.Transition.Error),
                    string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                    string.Empty, string.Empty, string.Empty, string.Empty));
                EnsureExportSize(builder);
            }

            foreach (var row in report.ResponseSamples)
            {
                builder.AppendLine(string.Join(',',
                    Csv("Check"), Csv(row.DeviceName), Csv(row.IpAddress), Csv(row.Location), string.Empty,
                    Csv(row.Sample.CheckedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
                    string.Empty, Csv(row.Sample.Status.ToString()),
                    Csv(row.Sample.ResponseTimeMilliseconds?.ToString(CultureInfo.InvariantCulture)), Csv(row.Sample.Error),
                    string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                    string.Empty, string.Empty, string.Empty, string.Empty));
                EnsureExportSize(builder);
            }

            return new(Encoding.UTF8.GetBytes(builder.ToString()), $"availability-report-{query.StartDate:yyyyMMdd}-{query.EndDate:yyyyMMdd}.csv");
        }

        builder.AppendLine("IpAddress,DeviceName,DeviceType,VlanId,VlanName,Location,Building,Group,SwitchName,SwitchIpAddress,SwitchPort,MacAddress,AssignedTo,Department,AssignmentStatus,Description");
        foreach (var record in report.IpamRecords)
        {
            builder.AppendLine(string.Join(',',
                Csv(record.IpAddress), Csv(record.DeviceName), Csv(record.DeviceType),
                Csv(record.VlanId?.ToString(CultureInfo.InvariantCulture)), Csv(record.VlanName),
                Csv(record.Location), Csv(record.Building), Csv(record.Group), Csv(record.SwitchName),
                Csv(record.SwitchIpAddress), Csv(record.SwitchPort), Csv(record.MacAddress),
                Csv(record.AssignedTo), Csv(record.Department), Csv(record.AssignmentStatus.ToString()), Csv(record.Description)));
            EnsureExportSize(builder);
        }

        return new(Encoding.UTF8.GetBytes(builder.ToString()), "ipam-inventory-report.csv");
    }

    private static (TimeSpan Up, TimeSpan Down, TimeSpan Unknown) CalculateDurations(
        MonitoringHistoryWindow window,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var up = TimeSpan.Zero;
        var down = TimeSpan.Zero;
        var unknown = TimeSpan.Zero;
        var cursor = start;
        var current = window.BeforeStart?.Status ?? DeviceStatus.Unknown;

        foreach (var transition in window.Transitions.OrderBy(item => item.OccurredUtc))
        {
            var occurred = transition.OccurredUtc < start ? start : transition.OccurredUtc > end ? end : transition.OccurredUtc;
            if (occurred > cursor) AddDuration(current, occurred - cursor, ref up, ref down, ref unknown);
            current = transition.Status;
            cursor = occurred;
        }

        if (end > cursor) AddDuration(current, end - cursor, ref up, ref down, ref unknown);
        return (up, down, unknown);
    }

    private static void AddDuration(DeviceStatus status, TimeSpan duration, ref TimeSpan up, ref TimeSpan down, ref TimeSpan unknown)
    {
        switch (status)
        {
            case DeviceStatus.Up: up += duration; break;
            case DeviceStatus.Down: down += duration; break;
            default: unknown += duration; break;
        }
    }

    private static string GetHistoryNote(MonitoringHistoryWindow window, DateTimeOffset startUtc)
    {
        if (window.FirstRecordedUtc is null) return "No recorded status transitions; availability is unavailable.";
        if (window.BeforeStart is null && window.FirstRecordedUtc > startUtc)
            return $"History starts {window.FirstRecordedUtc.Value:yyyy-MM-dd HH:mm:ss 'UTC'}; earlier duration is unverified.";
        return "Transition history available for the selected period.";
    }

    private static string GetResponseNote(MonitoringSampleWindow window, DateTimeOffset startUtc)
    {
        if (window.FirstRecordedUtc is null) return "No persisted check samples; response-time history is unavailable.";
        if (window.BeforeStart is null && window.FirstRecordedUtc > startUtc)
            return $"Check samples start {window.FirstRecordedUtc.Value:yyyy-MM-dd HH:mm:ss 'UTC'}; earlier response times are unavailable.";
        return "Persisted check samples available for the selected period.";
    }

    private static IReadOnlyList<ReportStatusSummary> BuildLocationSummary(
        IReadOnlyList<DeviceInfo> devices,
        IReadOnlyList<ManagedLocation> locations) =>
        locations.Select(location =>
        {
            var descendants = GetLocationDescendants(location.Id, locations);
            var matched = devices.Where(device =>
                (device.LocationId is { } locationId && descendants.Contains(locationId)) ||
                (device.BuildingId is { } buildingId && descendants.Contains(buildingId))).ToArray();
            return Summary(location.Name, InventoryStructureService.BuildLocationPath(locations, location.Id), matched);
        }).OrderBy(summary => summary.Path, StringComparer.OrdinalIgnoreCase).ToArray();

    private static IReadOnlyList<ReportStatusSummary> BuildGroupSummary(
        IReadOnlyList<DeviceInfo> devices,
        IReadOnlyList<ManagedGroup> groups) =>
        groups.Select(group =>
        {
            var descendants = GetGroupDescendants(group.Id, groups);
            var matched = devices.Where(device => device.GroupId is { } groupId && descendants.Contains(groupId)).ToArray();
            return Summary(group.Name, InventoryStructureService.BuildGroupPath(groups, group.Id), matched);
        }).OrderBy(summary => summary.Path, StringComparer.OrdinalIgnoreCase).ToArray();

    private static ReportStatusSummary Summary(string name, string path, IReadOnlyList<DeviceInfo> devices) => new(
        name,
        path,
        devices.Count,
        devices.Count(device => device.Status == DeviceStatus.Up),
        devices.Count(device => device.Status == DeviceStatus.Down),
        devices.Count(device => device.Status == DeviceStatus.Unknown));

    private static HashSet<Guid> GetLocationDescendants(Guid rootId, IReadOnlyList<ManagedLocation> locations)
    {
        var result = new HashSet<Guid> { rootId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var location in locations)
                if (location.ParentLocationId is { } parent && result.Contains(parent) && result.Add(location.Id)) changed = true;
        }
        return result;
    }

    private static HashSet<Guid> GetGroupDescendants(Guid rootId, IReadOnlyList<ManagedGroup> groups)
    {
        var result = new HashSet<Guid> { rootId };
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var group in groups)
                if (group.ParentGroupId is { } parent && result.Contains(parent) && result.Add(group.Id)) changed = true;
        }
        return result;
    }

    private static bool IsInSelectedLocation(DeviceInfo device, Guid? filterId, IReadOnlyList<ManagedLocation> locations)
    {
        if (!filterId.HasValue) return true;
        var descendants = GetLocationDescendants(filterId.Value, locations);
        return device.LocationId is { } locationId && descendants.Contains(locationId) ||
            device.BuildingId is { } buildingId && descendants.Contains(buildingId);
    }

    private static bool IsInSelectedGroup(DeviceInfo device, Guid? filterId, IReadOnlyList<ManagedGroup> groups)
    {
        if (!filterId.HasValue) return true;
        return device.GroupId is { } groupId && GetGroupDescendants(filterId.Value, groups).Contains(groupId);
    }

    private static bool IsInSelectedIpamLocation(IpamRecord record, Guid? filterId, IReadOnlyList<ManagedLocation> locations)
    {
        if (!filterId.HasValue) return true;
        var descendants = GetLocationDescendants(filterId.Value, locations);
        return record.LocationId is { } locationId && descendants.Contains(locationId) ||
            record.BuildingId is { } buildingId && descendants.Contains(buildingId);
    }

    private static bool IsInSelectedIpamGroup(IpamRecord record, Guid? filterId, IReadOnlyList<ManagedGroup> groups) =>
        !filterId.HasValue || record.GroupId is { } groupId && GetGroupDescendants(filterId.Value, groups).Contains(groupId);

    private static string? Validate(MonitoringReportQuery query)
    {
        if (query.StartDate > query.EndDate) return "The start date must be on or before the end date.";
        if (query.EndDate > DateOnly.FromDateTime(DateTime.UtcNow)) return "The end date cannot be in the future (UTC).";
        if (query.EndDate.DayNumber - query.StartDate.DayNumber > 365) return "Choose a date range of 366 days or less.";
        if (query.Status is { } status && !Enum.IsDefined(status)) return "Choose a supported device status.";
        if (query.IpAssignmentStatus is { } ipStatus && !Enum.IsDefined(ipStatus)) return "Choose a supported IP assignment status.";
        return null;
    }

    private static (DateTimeOffset Start, DateTimeOffset End) GetRange(MonitoringReportQuery query)
    {
        var start = new DateTimeOffset(query.StartDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var end = new DateTimeOffset(query.EndDate.ToDateTime(TimeOnly.MaxValue), TimeSpan.Zero);
        var now = DateTimeOffset.UtcNow;
        if (end > now) end = now;
        return (start, end);
    }

    private static MonitoringReportData Empty(MonitoringReportQuery query, string error) => new(
        query,
        DateTimeOffset.MinValue,
        DateTimeOffset.MinValue,
        [], [], [], [], [], [], [], [], [], error);

    private static string Csv(string? value)
    {
        if (value is null) return string.Empty;
        var firstNonWhitespace = value.AsSpan().TrimStart();
        if (!firstNonWhitespace.IsEmpty && firstNonWhitespace[0] is '=' or '+' or '-' or '@') value = $"'{value}";
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static void EnsureExportSize(StringBuilder builder)
    {
        if (builder.Length > MaximumExportCharacters)
            throw new ReportExportTooLargeException("The report export exceeds the 20 MB limit. Apply narrower filters.");
    }
}

public sealed class ReportExportTooLargeException(string message) : Exception(message);
