using System.Diagnostics;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UniversalITMonitor.Application.Devices;
using UniversalITMonitor.Application.Ipam;
using UniversalITMonitor.Application.Inventory;
using UniversalITMonitor.Application.Notifications;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;
using UniversalITMonitor.Web.Models;
using UniversalITMonitor.Web.ViewModels;

namespace UniversalITMonitor.Web.Controllers;

public class HomeController(
    IDeviceService devices,
    IJsonDocumentStore documents,
    IInventoryStructureService structure,
    IIpamService ipam,
    INotificationService notifications,
    IMonitoringHistoryStore history,
    IMonitoringSampleStore samples) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var allDevices = await devices.GetAllAsync(cancellationToken);
        var locations = await structure.GetLocationSummariesAsync(cancellationToken);
        var groups = await structure.GetGroupSummariesAsync(cancellationToken);
        var ipRecords = await ipam.GetRecordsAsync(cancellationToken);
        var recentNotifications = await notifications.GetRecentAsync(100, cancellationToken);
        var settings = await documents.ReadAsync("settings.json", static () => new ApplicationSettings(), cancellationToken);
        var nowUtc = DateTimeOffset.UtcNow;
        var historyStartUtc = nowUtc.AddDays(-7);
        var historyPointStarts = Enumerable.Range(0, 7)
            .Select(day => historyStartUtc.AddDays(day))
            .ToArray();
        var availabilityUpSeconds = new double[historyPointStarts.Length];
        var availabilityDownSeconds = new double[historyPointStarts.Length];
        var responseSamples = Enumerable.Range(0, historyPointStarts.Length)
            .Select(_ => new List<long>())
            .ToArray();
        var devicesWithAvailabilityHistory = 0;

        foreach (var device in allDevices)
        {
            var historyWindow = await history.GetWindowAsync(device.Id, historyStartUtc, nowUtc, cancellationToken);
            if (historyWindow.BeforeStart is not null || historyWindow.Transitions.Count > 0)
            {
                devicesWithAvailabilityHistory++;
            }

            var transitions = historyWindow.Transitions.OrderBy(item => item.OccurredUtc).ToArray();
            var transitionIndex = 0;
            var state = historyWindow.BeforeStart?.Status ?? DeviceStatus.Unknown;
            for (var day = 0; day < historyPointStarts.Length; day++)
            {
                var periodStart = historyPointStarts[day];
                var periodEnd = periodStart.AddDays(1);
                while (transitionIndex < transitions.Length && transitions[transitionIndex].OccurredUtc <= periodStart)
                {
                    state = transitions[transitionIndex].Status;
                    transitionIndex++;
                }

                var cursor = periodStart;
                while (transitionIndex < transitions.Length && transitions[transitionIndex].OccurredUtc < periodEnd)
                {
                    var transition = transitions[transitionIndex];
                    AddKnownDuration(state, transition.OccurredUtc - cursor, day, availabilityUpSeconds, availabilityDownSeconds);
                    state = transition.Status;
                    cursor = transition.OccurredUtc;
                    transitionIndex++;
                }

                AddKnownDuration(state, periodEnd - cursor, day, availabilityUpSeconds, availabilityDownSeconds);
            }

            var sampleWindow = await samples.GetWindowAsync(device.Id, historyStartUtc, nowUtc, cancellationToken);
            foreach (var sample in sampleWindow.Samples)
            {
                if (sample.Status != DeviceStatus.Up || sample.ResponseTimeMilliseconds is not { } response) continue;
                var period = (int)Math.Floor((sample.CheckedUtc - historyStartUtc).TotalDays);
                if (period >= 0 && period < responseSamples.Length)
                {
                    responseSamples[period].Add(response);
                }
            }
        }

        var availabilityHistory = historyPointStarts.Select((periodStart, index) =>
        {
            var knownSeconds = availabilityUpSeconds[index] + availabilityDownSeconds[index];
            var availability = knownSeconds == 0
                ? (double?)null
                : availabilityUpSeconds[index] / knownSeconds * 100;
            var coverage = allDevices.Count == 0
                ? 0
                : knownSeconds / (TimeSpan.FromDays(1).TotalSeconds * allDevices.Count) * 100;
            return new DashboardAvailabilityPoint(
                periodStart.ToString("MMM d", CultureInfo.InvariantCulture),
                periodStart,
                availability,
                Math.Clamp(coverage, 0, 100));
        }).ToArray();

        var totalUpSeconds = availabilityUpSeconds.Sum();
        var totalDownSeconds = availabilityDownSeconds.Sum();
        var totalKnownSeconds = totalUpSeconds + totalDownSeconds;
        var currentAvailability = totalKnownSeconds == 0
            ? (double?)null
            : totalUpSeconds / totalKnownSeconds * 100;
        var availabilityCoverage = allDevices.Count == 0
            ? (double?)null
            : Math.Clamp(totalKnownSeconds / (TimeSpan.FromDays(7).TotalSeconds * allDevices.Count) * 100, 0, 100);

        var maximumResponse = responseSamples
            .Where(bucket => bucket.Count > 0)
            .Select(bucket => bucket.Average())
            .DefaultIfEmpty(0)
            .Max();
        var responseHistory = historyPointStarts.Select((periodStart, index) =>
        {
            var bucket = responseSamples[index];
            var average = bucket.Count == 0 ? (long?)null : (long)Math.Round(bucket.Average());
            var height = average is null || maximumResponse <= 0 ? 0 : Math.Clamp(average.Value / maximumResponse * 100, 0, 100);
            return new DashboardResponsePoint(
                periodStart.ToString("MMM d", CultureInfo.InvariantCulture),
                periodStart,
                average,
                bucket.Count,
                height);
        }).ToArray();

        var currentResponseDevices = allDevices
            .Where(device => device.Status == DeviceStatus.Up && device.ResponseTimeMilliseconds.HasValue)
            .Select(device => device.ResponseTimeMilliseconds!.Value)
            .ToArray();
        var currentAverageResponse = currentResponseDevices.Length == 0
            ? (long?)null
            : (long)Math.Round(currentResponseDevices.Average());

        return View(new DashboardViewModel
        {
            Devices = allDevices.OrderByDescending(device => device.LastCheckUtc).ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase).Take(8).ToArray(),
            Servers = allDevices.Where(device => device.Type == DeviceType.Server)
                .OrderByDescending(device => device.LatestSnmpMetrics?.CollectedUtc ?? DateTimeOffset.MinValue)
                .ThenBy(device => device.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            RecentAlertTransitions = recentNotifications.Where(record => record.Kind == NotificationRecordKind.DeviceTransition).Take(6).ToArray(),
            AvailabilityHistory = availabilityHistory,
            ResponseHistory = responseHistory,
            TotalCount = allDevices.Count,
            UpCount = allDevices.Count(device => device.Status == DeviceStatus.Up),
            DownCount = allDevices.Count(device => device.Status == DeviceStatus.Down),
            UnknownCount = allDevices.Count(device => device.Status == DeviceStatus.Unknown),
            MonitoringEnabledCount = allDevices.Count(device => device.MonitoringEnabled),
            PollingIntervalSeconds = Math.Clamp(settings.PollingIntervalSeconds, 10, 3600),
            Locations = locations.OrderByDescending(location => location.DeviceCount).Take(5).ToArray(),
            Groups = groups.OrderByDescending(group => group.DeviceCount).Take(5).ToArray(),
            IpamAddressCount = ipRecords.Count,
            IpamAssignedCount = ipRecords.Count(record => record.AssignmentStatus == IpAssignmentStatus.Assigned),
            IpamAvailableCount = ipRecords.Count(record => record.AssignmentStatus == IpAssignmentStatus.Available),
            IpamReservedCount = ipRecords.Count(record => record.AssignmentStatus == IpAssignmentStatus.Reserved),
            IpamConflictCount = ipRecords.Count(record => record.AssignmentStatus == IpAssignmentStatus.Conflict),
            RecentNotificationEventCount = recentNotifications.Count(record => record.Kind == NotificationRecordKind.DeviceTransition),
            RecentNotificationFailureCount = recentNotifications.Sum(record => record.Deliveries.Count(delivery => !delivery.Succeeded)),
            CurrentAvailabilityPercent = currentAvailability,
            AvailabilityCoveragePercent = availabilityCoverage,
            DevicesWithAvailabilityHistory = devicesWithAvailabilityHistory,
            AverageResponseTimeMilliseconds = currentAverageResponse,
            DevicesWithCurrentResponse = currentResponseDevices.Length,
            ResponseSampleCount = responseSamples.Sum(bucket => bucket.Count),
            UpPercent = GetStatusPercent(allDevices.Count(device => device.Status == DeviceStatus.Up), allDevices.Count),
            DownPercent = GetStatusPercent(allDevices.Count(device => device.Status == DeviceStatus.Down), allDevices.Count),
            UnknownPercent = GetStatusPercent(allDevices.Count(device => device.Status == DeviceStatus.Unknown), allDevices.Count)
        });
    }

    private static void AddKnownDuration(
        DeviceStatus state,
        TimeSpan duration,
        int day,
        double[] upSeconds,
        double[] downSeconds)
    {
        if (duration <= TimeSpan.Zero) return;
        if (state == DeviceStatus.Up) upSeconds[day] += duration.TotalSeconds;
        else if (state == DeviceStatus.Down) downSeconds[day] += duration.TotalSeconds;
    }

    private static double GetStatusPercent(int count, int total) =>
        total == 0 ? 0 : count / (double)total * 100;

    [AllowAnonymous]
    public IActionResult Privacy()
    {
        return View();
    }

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
