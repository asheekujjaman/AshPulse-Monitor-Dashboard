using System.Collections.Concurrent;
using System.Text.Json;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Storage.Json;

public sealed class JsonSnmpMetricsStore : ISnmpMetricsStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly string _metricsDirectory;

    public JsonSnmpMetricsStore(string dataDirectory)
    {
        _metricsDirectory = Path.Combine(Path.GetFullPath(dataDirectory), "snmp-metrics");
        Directory.CreateDirectory(_metricsDirectory);
    }

    public async Task<IReadOnlyDictionary<Guid, SnmpMetrics>> GetManyAsync(
        IEnumerable<Guid> deviceIds,
        CancellationToken cancellationToken = default)
    {
        var ids = deviceIds.Distinct().ToArray();
        var results = await Task.WhenAll(ids.Select(async id =>
            (Id: id, Metrics: await ReadAsync(id, cancellationToken))));
        return results.Where(result => result.Metrics is not null)
            .ToDictionary(result => result.Id, result => result.Metrics!);
    }

    public async Task WriteAsync(Guid deviceId, SnmpMetrics metrics, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(deviceId);
        await WithLockAsync(path, async () =>
        {
            var payload = JsonSerializer.SerializeToUtf8Bytes(metrics, JsonOptions);
            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                                 FileOptions.Asynchronous | FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(payload, cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }

                if (File.Exists(path))
                {
                    File.Copy(path, path + ".bak", overwrite: true);
                }

                File.Move(temporaryPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return true;
        }, cancellationToken);
    }

    public async Task DeleteAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(deviceId);
        await WithLockAsync(path, () =>
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            return Task.FromResult(true);
        }, cancellationToken);
    }

    private async Task<SnmpMetrics?> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        var path = ResolvePath(id);
        return await WithLockAsync(path, async () =>
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return await DeserializeAsync(path, cancellationToken);
            }
            catch (JsonException primaryError)
            {
                var backupPath = path + ".bak";
                if (!File.Exists(backupPath))
                {
                    throw new InvalidDataException($"SNMP metrics for device '{id}' are invalid and no backup is available.", primaryError);
                }

                SnmpMetrics backup;
                try
                {
                    backup = await DeserializeAsync(backupPath, cancellationToken);
                }
                catch (JsonException backupError)
                {
                    throw new InvalidDataException($"SNMP metrics and the backup for device '{id}' are invalid. Both files were preserved.", new AggregateException(primaryError, backupError));
                }

                File.Move(path, path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff"));
                File.Copy(backupPath, path);
                return backup;
            }
        }, cancellationToken);
    }

    private static async Task<SnmpMetrics> DeserializeAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<SnmpMetrics>(stream, JsonOptions, cancellationToken)
               ?? throw new JsonException("The SNMP metrics document is null.");
    }

    private async Task<T> WithLockAsync<T>(string path, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        var semaphore = LocalLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            await using var processLock = await AcquireProcessLockAsync(path + ".lock", cancellationToken);
            return await action();
        }
        finally
        {
            semaphore.Release();
        }
    }

    private string ResolvePath(Guid id) => Path.Combine(_metricsDirectory, id.ToString("N") + ".json");

    private static async Task<FileStream> AcquireProcessLockAsync(string lockPath, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(50, cancellationToken);
            }
        }
    }
}
