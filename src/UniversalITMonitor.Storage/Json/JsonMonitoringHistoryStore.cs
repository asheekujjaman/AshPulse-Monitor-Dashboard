using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Storage.Json;

public sealed class JsonMonitoringHistoryStore : IMonitoringHistoryStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _historyDirectory;

    public JsonMonitoringHistoryStore(string dataDirectory)
    {
        _historyDirectory = Path.Combine(Path.GetFullPath(dataDirectory), "monitoring-history");
        Directory.CreateDirectory(_historyDirectory);
    }

    public async Task AppendAsync(MonitoringHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(entry.DeviceId);
        var gate = LocalLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var processLock = await AcquireProcessLockAsync(path + ".lock", cancellationToken);
            if (await ContainsTransitionAsync(path, entry.Id, cancellationToken))
            {
                return;
            }

            var line = JsonSerializer.SerializeToUtf8Bytes(entry, JsonOptions);
            await using var stream = new FileStream(
                path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.WriteThrough);
            await stream.WriteAsync(line, cancellationToken);
            await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<IReadOnlyList<MonitoringHistoryEntry>> GetForDeviceAsync(
        Guid deviceId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(deviceId);
        if (!File.Exists(path))
        {
            return [];
        }

        limit = Math.Clamp(limit, 1, 500);
        var entries = new Queue<MonitoringHistoryEntry>(limit);
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize<MonitoringHistoryEntry>(line, JsonOptions);
                if (entry is null || entry.DeviceId != deviceId)
                {
                    continue;
                }

                if (entries.Count == limit)
                {
                    entries.Dequeue();
                }

                entries.Enqueue(entry);
            }
            catch (JsonException)
            {
                // Each JSONL record is independent; preserve readable records around a damaged line.
            }
        }

        return entries.Reverse().ToArray();
    }

    public async Task<MonitoringHistoryWindow> GetWindowAsync(
        Guid deviceId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default)
    {
        if (endUtc < startUtc)
        {
            return new(null, [], null);
        }

        var path = ResolvePath(deviceId);
        if (!File.Exists(path))
        {
            return new(null, [], null);
        }

        MonitoringHistoryEntry? before = null;
        DateTimeOffset? firstRecorded = null;
        var inRange = new List<MonitoringHistoryEntry>();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<MonitoringHistoryEntry>(line, JsonOptions);
                if (entry is null || entry.DeviceId != deviceId) continue;
                if (firstRecorded is null || entry.OccurredUtc < firstRecorded) firstRecorded = entry.OccurredUtc;
                if (entry.OccurredUtc < startUtc)
                {
                    if (before is null || entry.OccurredUtc > before.OccurredUtc) before = entry;
                }
                else if (entry.OccurredUtc <= endUtc)
                {
                    inRange.Add(entry);
                }
            }
            catch (JsonException)
            {
                // Keep valid transition records around a damaged JSONL line.
            }
        }

        inRange.Sort(static (left, right) => left.OccurredUtc.CompareTo(right.OccurredUtc));
        return new(before, inRange, firstRecorded);
    }

    private string ResolvePath(Guid id) => Path.Combine(_historyDirectory, id.ToString("N") + ".jsonl");

    private static async Task<bool> ContainsTransitionAsync(string path, Guid transitionId, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return false;

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var existing = JsonSerializer.Deserialize<MonitoringHistoryEntry>(line, JsonOptions);
                if (existing?.Id == transitionId) return true;
            }
            catch (JsonException)
            {
                // A damaged record does not prevent idempotent recovery of valid entries.
            }
        }

        return false;
    }

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
