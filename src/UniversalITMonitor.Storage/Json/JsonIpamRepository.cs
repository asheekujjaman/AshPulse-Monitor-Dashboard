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
                if (document.Records.Any(existing => existing.Id != record.Id &&
                    string.Equals(NormalizeIp(existing.IpAddress), normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    return (document, IpamWriteResult.DuplicateIpAddress);
                }

                var index = document.Records.FindIndex(existing => existing.Id == record.Id);
                if (index < 0 && record.Id != Guid.Empty) return (document, IpamWriteResult.NotFound);
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

                return (document, IpamWriteResult.Success);
            },
            cancellationToken);

    public Task<bool> DeleteRecordAsync(Guid id, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<IpamDocument, bool>(
            DocumentName,
            static () => new IpamDocument(),
            document => (document, document.Records.RemoveAll(record => record.Id == id) > 0),
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

    public Task<IpamWriteResult> SaveSubnetAsync(IpamSubnet subnet, CancellationToken cancellationToken = default) =>
        documents.UpdateAsync<IpamDocument, IpamWriteResult>(
            DocumentName,
            static () => new IpamDocument(),
            document =>
            {
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
}
