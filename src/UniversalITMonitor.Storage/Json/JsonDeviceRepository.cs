using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Storage.Json;

public sealed class JsonDeviceRepository(IJsonDocumentStore documents) : IDeviceRepository
{
    private const string DocumentName = "devices.json";

    public async Task<IReadOnlyList<NetworkDevice>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var document = await documents.ReadAsync(DocumentName, static () => new DeviceDocument(), cancellationToken);
        return document.Devices.OrderBy(device => device.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<NetworkDevice?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await documents.ReadAsync(DocumentName, static () => new DeviceDocument(), cancellationToken);
        return document.Devices.FirstOrDefault(device => device.Id == id);
    }

    public Task<DeviceWriteResult> CreateAsync(NetworkDevice device, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<DeviceDocument, DeviceWriteResult>(
            DocumentName,
            static () => new DeviceDocument(),
            document =>
            {
                if (document.Devices.Any(existing => IpEquals(existing.IpAddress, device.IpAddress)))
                {
                    return (document, DeviceWriteResult.DuplicateIpAddress);
                }

                document.Devices.Add(device);
                return (document, DeviceWriteResult.Success);
            },
            cancellationToken);

    public Task<DeviceWriteResult> UpdateAsync(NetworkDevice device, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<DeviceDocument, DeviceWriteResult>(
            DocumentName,
            static () => new DeviceDocument(),
            document =>
            {
                var index = document.Devices.FindIndex(existing => existing.Id == device.Id);
                if (index < 0)
                {
                    return (document, DeviceWriteResult.NotFound);
                }

                if (document.Devices.Any(existing => existing.Id != device.Id && IpEquals(existing.IpAddress, device.IpAddress)))
                {
                    return (document, DeviceWriteResult.DuplicateIpAddress);
                }

                var existing = document.Devices[index];
                device.CreatedUtc = existing.CreatedUtc;
                device.UpdatedUtc = DateTimeOffset.UtcNow;
                if (!IpEquals(existing.IpAddress, device.IpAddress))
                {
                    ResetMonitoringState(device);
                }
                else
                {
                    PreserveMonitoringState(existing, device);
                }

                document.Devices[index] = device;
                return (document, DeviceWriteResult.Success);
            },
            cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<DeviceDocument, bool>(
            DocumentName,
            static () => new DeviceDocument(),
            document =>
            {
                var removed = document.Devices.RemoveAll(device => device.Id == id) > 0;
                return (document, removed);
            },
            cancellationToken);

    public Task<DeviceWriteResult> SetMonitoringEnabledAsync(
        Guid id,
        bool enabled,
        CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<DeviceDocument, DeviceWriteResult>(
            DocumentName,
            static () => new DeviceDocument(),
            document =>
            {
                var device = document.Devices.FirstOrDefault(candidate => candidate.Id == id);
                if (device is null)
                {
                    return (document, DeviceWriteResult.NotFound);
                }

                device.MonitoringEnabled = enabled;
                device.UpdatedUtc = DateTimeOffset.UtcNow;
                device.Status = DeviceStatus.Unknown;
                device.CurrentStateSinceUtc = null;
                device.ResponseTimeMilliseconds = null;
                device.LastCheckError = null;
                return (document, DeviceWriteResult.Success);
            },
            cancellationToken);

    public Task<DeviceCheckBatchResult> RecordChecksAsync(
        IReadOnlyCollection<DeviceCheckResult> results,
        CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<DeviceDocument, DeviceCheckBatchResult>(
            DocumentName,
            static () => new DeviceDocument(),
            document =>
            {
                var events = new List<MonitoringHistoryEntry>();
                var appliedIds = new List<Guid>();
                var devices = document.Devices.ToDictionary(device => device.Id);
                foreach (var result in results)
                {
                    if (!devices.TryGetValue(result.DeviceId, out var device) || !device.MonitoringEnabled ||
                        !IpEquals(device.IpAddress, result.IpAddress))
                    {
                        continue;
                    }

                    appliedIds.Add(device.Id);

                    var previousStatus = device.Status;
                    if (device.Status != result.Status)
                    {
                        events.Add(new MonitoringHistoryEntry
                        {
                            DeviceId = device.Id,
                            DeviceName = device.Name,
                            IpAddress = device.IpAddress,
                            Location = device.Location,
                            Group = device.Group,
                            OccurredUtc = result.CheckedUtc,
                            PreviousStatus = previousStatus,
                            Status = result.Status,
                            ResponseTimeMilliseconds = result.ResponseTimeMilliseconds,
                            Error = result.Error
                        });

                        device.Status = result.Status;
                        device.CurrentStateSinceUtc = result.CheckedUtc;
                    }

                    device.LastCheckUtc = result.CheckedUtc;
                    device.ResponseTimeMilliseconds = result.Status == DeviceStatus.Up ? result.ResponseTimeMilliseconds : null;
                    device.LastCheckError = result.Error;
                    if (result.Status == DeviceStatus.Up)
                    {
                        device.LastSuccessfulCheckUtc = result.CheckedUtc;
                    }
                    else if (result.Status == DeviceStatus.Down)
                    {
                        device.LastFailedCheckUtc = result.CheckedUtc;
                    }

                }

                document.PendingTransitions ??= [];
                document.PendingTransitions.AddRange(events);

                return (document, new DeviceCheckBatchResult { AppliedDeviceIds = appliedIds, Transitions = events });
            },
            cancellationToken);

    public async Task<IReadOnlyList<MonitoringHistoryEntry>> GetPendingTransitionsAsync(CancellationToken cancellationToken = default)
    {
        var document = await documents.ReadAsync(DocumentName, static () => new DeviceDocument(), cancellationToken);
        return (document.PendingTransitions ?? []).OrderBy(item => item.OccurredUtc).ToArray();
    }

    public Task<bool> CompleteTransitionAsync(Guid transitionId, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<DeviceDocument, bool>(
            DocumentName,
            static () => new DeviceDocument(),
            document =>
            {
                document.PendingTransitions ??= [];
                var removed = document.PendingTransitions.RemoveAll(item => item.Id == transitionId) > 0;
                return (document, removed);
            },
            cancellationToken);

    public Task<DeviceImportResult> ImportAsync(
        IReadOnlyCollection<NetworkDevice> devices,
        CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<DeviceDocument, DeviceImportResult>(
            DocumentName,
            static () => new DeviceDocument(),
            document =>
            {
                var errors = new List<string>();
                var usedAddresses = new HashSet<string>(document.Devices.Select(device => device.IpAddress), StringComparer.OrdinalIgnoreCase);
                var imported = 0;
                foreach (var device in devices)
                {
                    if (!usedAddresses.Add(device.IpAddress))
                    {
                        errors.Add($"{device.Name} ({device.IpAddress}) was skipped because that IP address is already in use.");
                        continue;
                    }

                    document.Devices.Add(device);
                    imported++;
                }

                return (document, new DeviceImportResult(imported, errors));
            },
            cancellationToken);

    public Task UpdateAssignmentsAsync(
        IReadOnlyCollection<DeviceAssignmentUpdate> assignments,
        CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<DeviceDocument, bool>(
            DocumentName,
            static () => new DeviceDocument(),
            document =>
            {
                var updates = assignments.ToDictionary(item => item.DeviceId);
                foreach (var device in document.Devices)
                {
                    if (!updates.TryGetValue(device.Id, out var assignment))
                    {
                        continue;
                    }

                    device.LocationId = assignment.LocationId;
                    device.BuildingId = assignment.BuildingId;
                    device.GroupId = assignment.GroupId;
                    device.Location = assignment.Location;
                    device.Building = assignment.Building;
                    device.Group = assignment.Group;
                    device.UpdatedUtc = DateTimeOffset.UtcNow;
                }

                return (document, true);
            },
            cancellationToken);

    private static bool IpEquals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void ResetMonitoringState(NetworkDevice device)
    {
        device.Status = DeviceStatus.Unknown;
        device.LastCheckUtc = null;
        device.LastSuccessfulCheckUtc = null;
        device.LastFailedCheckUtc = null;
        device.CurrentStateSinceUtc = null;
        device.ResponseTimeMilliseconds = null;
        device.LastCheckError = null;
        device.LatestSnmpMetrics = null;
    }

    private static void PreserveMonitoringState(NetworkDevice existing, NetworkDevice updated)
    {
        updated.Status = existing.Status;
        updated.LastCheckUtc = existing.LastCheckUtc;
        updated.LastSuccessfulCheckUtc = existing.LastSuccessfulCheckUtc;
        updated.LastFailedCheckUtc = existing.LastFailedCheckUtc;
        updated.CurrentStateSinceUtc = existing.CurrentStateSinceUtc;
        updated.ResponseTimeMilliseconds = existing.ResponseTimeMilliseconds;
        updated.LastCheckError = existing.LastCheckError;
        updated.LatestSnmpMetrics = existing.LatestSnmpMetrics;
    }
}
