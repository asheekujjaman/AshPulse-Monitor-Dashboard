using System.Net;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Storage.Json;

public sealed class JsonIpamRepository(IJsonDocumentStore documents) : IIpamRepository
{
    private const string DocumentName = "ipam.json";

    public async Task<IReadOnlyList<IpamRecord>> GetRecordsAsync(CancellationToken cancellationToken = default)
    {
        var document = await ReadAsync(cancellationToken);
        return document.Records.OrderBy(record => record.IpAddress, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IpamRecord?> GetRecordAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await ReadAsync(cancellationToken);
        return document.Records.FirstOrDefault(record => record.Id == id);
    }

    public Task<IpamWriteResult> SaveRecordAsync(IpamRecord record, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<IpamDocument, IpamWriteResult>(
            DocumentName,
            static () => new IpamDocument(),
            document =>
            {
                var normalized = NormalizeIp(record.IpAddress);
                if (normalized is null) return (document, IpamWriteResult.Invalid);
                if (record.SubnetId is { } subnetId)
                {
                    var subnet = document.Subnets.FirstOrDefault(item => item.Id == subnetId);
                    if (subnet is null || !IsInCidr(normalized, subnet.Cidr)) return (document, IpamWriteResult.Invalid);
                }
                if (document.Records.Any(existing => existing.Id != record.Id &&
                    string.Equals(NormalizeIp(existing.IpAddress), normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    return (document, IpamWriteResult.DuplicateIpAddress);
                }

                var index = document.Records.FindIndex(existing => existing.Id == record.Id);
                if (index < 0 && record.Id != Guid.Empty) return (document, IpamWriteResult.NotFound);
                IpAssignmentStatus? previousStatus = null;
                string? previousOwner = null;
                if (index >= 0)
                {
                    var previous = document.Records[index];
                    previousStatus = previous.AssignmentStatus;
                    previousOwner = previous.AssignedTo;
                    if (previous.AssignmentStatus != record.AssignmentStatus)
                    {
                        var allowedTransition = record.AssignmentStatus switch
                        {
                            IpAssignmentStatus.Assigned => previous.AssignmentStatus is IpAssignmentStatus.Available or IpAssignmentStatus.Reserved,
                            IpAssignmentStatus.Reserved => previous.AssignmentStatus == IpAssignmentStatus.Available,
                            IpAssignmentStatus.Available => previous.AssignmentStatus is IpAssignmentStatus.Assigned or IpAssignmentStatus.Reserved or IpAssignmentStatus.Conflict or IpAssignmentStatus.Unknown,
                            IpAssignmentStatus.Conflict or IpAssignmentStatus.Unknown => true,
                            _ => false
                        };
                        if (!allowedTransition || (record.AssignmentStatus == IpAssignmentStatus.Assigned && previous.AssignmentStatus == IpAssignmentStatus.Reserved &&
                            !string.IsNullOrWhiteSpace(previous.AssignedTo) && !string.Equals(previous.AssignedTo, record.AssignedTo, StringComparison.OrdinalIgnoreCase)))
                            return (document, IpamWriteResult.AllocationConflict);
                    }
                }
                if (index >= 0 && record.AssignmentStatus == IpAssignmentStatus.Available && previousStatus != IpAssignmentStatus.Available)
                {
                    record.AssignedTo = null;
                    record.ReservationNote = null;
                    record.ReservedUntilUtc = null;
                    record.DeviceId = null;
                }
                record.IpAddress = normalized;
                if (index < 0)
                {
                    record.Id = Guid.NewGuid();
                    record.CreatedUtc = DateTimeOffset.UtcNow;
                    record.UpdatedUtc = record.CreatedUtc;
                    document.Records.Add(record);
                }
                else
                {
                    record.CreatedUtc = document.Records[index].CreatedUtc;
                    record.UpdatedUtc = DateTimeOffset.UtcNow;
                    document.Records[index] = record;
                }

                document.History.Add(new IpamHistoryEvent
                {
                    RecordId = record.Id,
                    IpAddress = normalized,
                    Action = index < 0 ? "Created" : "Updated",
                    PreviousStatus = previousStatus,
                    NewStatus = record.AssignmentStatus,
                    Actor = record.UpdatedBy,
                    Details = previousOwner is not null && !string.Equals(previousOwner, record.AssignedTo, StringComparison.OrdinalIgnoreCase)
                        ? $"Assigned to changed from {previousOwner} to {record.AssignedTo ?? "(none)"}"
                        : null,
                    OccurredUtc = DateTimeOffset.UtcNow
                });

                return (document, IpamWriteResult.Success);
            },
            cancellationToken);

    public Task<IpamWriteResult> ChangeAllocationAsync(IpamRecord record, IpamHistoryEvent history, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<IpamDocument, IpamWriteResult>(
            DocumentName,
            static () => new IpamDocument(),
            document =>
            {
                var normalized = NormalizeIp(record.IpAddress);
                if (normalized is null || record.SubnetId is not { } subnetId) return (document, IpamWriteResult.Invalid);
                var subnet = document.Subnets.FirstOrDefault(item => item.Id == subnetId);
                if (subnet is null || !IsInCidr(normalized, subnet.Cidr)) return (document, IpamWriteResult.Invalid);

                var existing = document.Records.FirstOrDefault(item =>
                    string.Equals(NormalizeIp(item.IpAddress), normalized, StringComparison.OrdinalIgnoreCase));
                var previous = existing?.AssignmentStatus;
                var previousOwner = existing?.AssignedTo;
                var allowed = existing is null
                    ? record.AssignmentStatus is IpAssignmentStatus.Assigned or IpAssignmentStatus.Reserved
                    : record.AssignmentStatus switch
                    {
                        IpAssignmentStatus.Assigned => previous is IpAssignmentStatus.Available or IpAssignmentStatus.Reserved,
                        IpAssignmentStatus.Reserved => previous == IpAssignmentStatus.Available,
                        IpAssignmentStatus.Available => previous is IpAssignmentStatus.Assigned or IpAssignmentStatus.Reserved or IpAssignmentStatus.Conflict or IpAssignmentStatus.Unknown,
                        _ => false
                    };
                if (existing is not null && record.AssignmentStatus == IpAssignmentStatus.Assigned &&
                    previous == IpAssignmentStatus.Reserved && !string.IsNullOrWhiteSpace(existing.AssignedTo) &&
                    !string.Equals(existing.AssignedTo, record.AssignedTo, StringComparison.OrdinalIgnoreCase))
                {
                    allowed = false;
                }
                if (!allowed) return (document, IpamWriteResult.AllocationConflict);

                var now = DateTimeOffset.UtcNow;
                if (existing is null)
                {
                    record.Id = Guid.NewGuid();
                    record.IpAddress = normalized;
                    record.CreatedUtc = now;
                    record.UpdatedUtc = now;
                    document.Records.Add(record);
                    existing = record;
                }
                else
                {
                    record.Id = existing.Id;
                    record.IpAddress = normalized;
                    existing.AssignmentStatus = record.AssignmentStatus;
                    existing.AssignedTo = record.AssignmentStatus == IpAssignmentStatus.Available
                        ? null
                        : string.IsNullOrWhiteSpace(record.AssignedTo) ? existing.AssignedTo : record.AssignedTo;
                    existing.ReservationNote = record.AssignmentStatus == IpAssignmentStatus.Available ? null : record.ReservationNote ?? existing.ReservationNote;
                    existing.ReservedUntilUtc = record.AssignmentStatus == IpAssignmentStatus.Available ? null : record.ReservedUntilUtc ?? existing.ReservedUntilUtc;
                    if (record.AssignmentStatus == IpAssignmentStatus.Available) existing.DeviceId = null;
                    existing.UpdatedUtc = now;
                }

                history.Id = Guid.NewGuid();
                history.RecordId = existing.Id;
                history.IpAddress = normalized;
                history.PreviousStatus = previous;
                history.NewStatus = record.AssignmentStatus;
                if (string.IsNullOrWhiteSpace(history.Details) && !string.IsNullOrWhiteSpace(previousOwner)) history.Details = $"Previously assigned to {previousOwner}";
                history.OccurredUtc = now;
                document.History.Add(history);
                record.Id = existing.Id;
                return (document, IpamWriteResult.Success);
            },
            cancellationToken);

    public Task<bool> DeleteRecordAsync(Guid id, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<IpamDocument, bool>(
            DocumentName,
            static () => new IpamDocument(),
            document =>
            {
                var record = document.Records.FirstOrDefault(item => item.Id == id);
                if (record is null) return (document, false);
                document.History.Add(new IpamHistoryEvent
                {
                    RecordId = record.Id,
                    IpAddress = record.IpAddress,
                    Action = "Deleted",
                    PreviousStatus = record.AssignmentStatus,
                    OccurredUtc = DateTimeOffset.UtcNow
                });
                document.Records.Remove(record);
                return (document, true);
            },
            cancellationToken);

    public Task<int> DetachDeviceAsync(Guid deviceId, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<IpamDocument, int>(
            DocumentName,
            static () => new IpamDocument(),
            document =>
            {
                var detached = 0;
                foreach (var record in document.Records.Where(record => record.DeviceId == deviceId))
                {
                    record.DeviceId = null;
                    record.UpdatedUtc = DateTimeOffset.UtcNow;
                    detached++;
                }

                return (document, detached);
            },
            cancellationToken);

    public Task<IpamImportStorageResult> AddRecordsIfUniqueAsync(IReadOnlyList<IpamRecord> records, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<IpamDocument, IpamImportStorageResult>(
            DocumentName,
            static () => new IpamDocument(),
            document =>
            {
                var known = document.Records.Select(record => NormalizeIp(record.IpAddress)).Where(ip => ip is not null).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var imported = 0;
                foreach (var record in records)
                {
                    var normalized = NormalizeIp(record.IpAddress);
                    if (normalized is null) continue;
                    if (!known.Add(normalized))
                    {
                        duplicates.Add(normalized);
                        continue;
                    }

                    record.IpAddress = normalized;
                    record.Id = record.Id == Guid.Empty ? Guid.NewGuid() : record.Id;
                    record.CreatedUtc = DateTimeOffset.UtcNow;
                    record.UpdatedUtc = record.CreatedUtc;
                    document.Records.Add(record);
                    document.History.Add(new IpamHistoryEvent
                    {
                        RecordId = record.Id,
                        IpAddress = normalized,
                        Action = "Imported",
                        Actor = "CSV import",
                        NewStatus = record.AssignmentStatus,
                        OccurredUtc = record.CreatedUtc
                    });
                    imported++;
                }

                return (document, new IpamImportStorageResult(imported, duplicates.Order(StringComparer.OrdinalIgnoreCase).ToArray()));
            },
            cancellationToken);

    public async Task<IReadOnlyList<IpamSubnet>> GetSubnetsAsync(CancellationToken cancellationToken = default)
    {
        var document = await ReadAsync(cancellationToken);
        return document.Subnets.OrderBy(subnet => subnet.Cidr, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<IpamSubnet?> GetSubnetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var document = await ReadAsync(cancellationToken);
        return document.Subnets.FirstOrDefault(subnet => subnet.Id == id);
    }

    public async Task<IReadOnlyList<IpamHistoryEvent>> GetHistoryAsync(CancellationToken cancellationToken = default)
    {
        var document = await ReadAsync(cancellationToken);
        return document.History.OrderByDescending(item => item.OccurredUtc).ToArray();
    }

    public Task<IpamWriteResult> SaveSubnetAsync(IpamSubnet subnet, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<IpamDocument, IpamWriteResult>(
            DocumentName,
            static () => new IpamDocument(),
            document =>
            {
                if (document.Records.Any(record => record.SubnetId == subnet.Id && !IsInCidr(record.IpAddress, subnet.Cidr)))
                    return (document, IpamWriteResult.Invalid);
                if (document.Subnets.Any(existing => existing.Id != subnet.Id &&
                    string.Equals(existing.Cidr, subnet.Cidr, StringComparison.OrdinalIgnoreCase)))
                {
                    return (document, IpamWriteResult.DuplicateSubnet);
                }

                var index = document.Subnets.FindIndex(existing => existing.Id == subnet.Id);
                if (index < 0 && subnet.Id != Guid.Empty) return (document, IpamWriteResult.NotFound);
                if (index < 0)
                {
                    subnet.Id = Guid.NewGuid();
                    subnet.CreatedUtc = DateTimeOffset.UtcNow;
                    subnet.UpdatedUtc = subnet.CreatedUtc;
                    document.Subnets.Add(subnet);
                }
                else
                {
                    subnet.CreatedUtc = document.Subnets[index].CreatedUtc;
                    subnet.UpdatedUtc = DateTimeOffset.UtcNow;
                    document.Subnets[index] = subnet;
                }

                return (document, IpamWriteResult.Success);
            },
            cancellationToken);

    public Task<IpamWriteResult> DeleteSubnetAsync(Guid id, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<IpamDocument, IpamWriteResult>(
            DocumentName,
            static () => new IpamDocument(),
            document =>
            {
                var subnet = document.Subnets.FirstOrDefault(existing => existing.Id == id);
                if (subnet is null) return (document, IpamWriteResult.NotFound);
                if (document.Records.Any(record => record.SubnetId == id)) return (document, IpamWriteResult.InUse);
                document.Subnets.Remove(subnet);
                return (document, IpamWriteResult.Success);
            },
            cancellationToken);

    private Task<IpamDocument> ReadAsync(CancellationToken cancellationToken) =>
        documents.ReadAsync(DocumentName, static () => new IpamDocument(), cancellationToken);

    private static string? NormalizeIp(string? value) =>
        IPAddress.TryParse(value, out var address) ? address.ToString() : null;

    private static bool IsInCidr(string ip, string cidr)
    {
        var parts = cidr.Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(ip, out var address) || !IPAddress.TryParse(parts[0], out var network) ||
            !int.TryParse(parts[1], out var prefix) || address.GetAddressBytes().Length != network.GetAddressBytes().Length) return false;
        var left = address.GetAddressBytes();
        var right = network.GetAddressBytes();
        for (var i = 0; i < left.Length; i++)
        {
            var bits = prefix - (i * 8);
            var mask = bits >= 8 ? 255 : bits <= 0 ? 0 : 255 << (8 - bits) & 255;
            if ((left[i] & mask) != (right[i] & mask)) return false;
        }
        return true;
    }
}
