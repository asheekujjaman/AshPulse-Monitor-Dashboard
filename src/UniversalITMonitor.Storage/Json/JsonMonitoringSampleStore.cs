using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using UniversalITMonitor.Core.Models;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Storage.Json;

public sealed class JsonMonitoringSampleStore : IMonitoringSampleStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalLocks = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _sampleDirectory;

    public JsonMonitoringSampleStore(string dataDirectory)
    {
        _sampleDirectory = Path.Combine(Path.GetFullPath(dataDirectory), "monitoring-samples");
        Directory.CreateDirectory(_sampleDirectory);
    }

    public async Task AppendAsync(MonitoringCheckSample sample, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(sample.DeviceId);
        var gate = LocalLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            await using var processLock = await AcquireProcessLockAsync(path + ".lock", cancellationToken);
            var line = JsonSerializer.SerializeToUtf8Bytes(sample, JsonOptions);
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

    public async Task<MonitoringSampleWindow> GetWindowAsync(
        Guid deviceId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc,
        CancellationToken cancellationToken = default)
    {
        if (endUtc < startUtc) return new([], null, null);
        var path = ResolvePath(deviceId);
        if (!File.Exists(path)) return new([], null, null);

        MonitoringCheckSample? before = null;
        DateTimeOffset? firstRecorded = null;
        var samples = new List<MonitoringCheckSample>();
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var sample = JsonSerializer.Deserialize<MonitoringCheckSample>(line, JsonOptions);
                if (sample is null || sample.DeviceId != deviceId) continue;
                if (firstRecorded is null || sample.CheckedUtc < firstRecorded) firstRecorded = sample.CheckedUtc;
                if (sample.CheckedUtc < startUtc)
                {
                    if (before is null || sample.CheckedUtc > before.CheckedUtc) before = sample;
                }
                else if (sample.CheckedUtc <= endUtc)
                {
                    samples.Add(sample);
                }
            }
            catch (JsonException)
            {
                // A damaged line does not hide readable samples around it.
            }
        }

        samples.Sort(static (left, right) => left.CheckedUtc.CompareTo(right.CheckedUtc));
        return new(samples, before, firstRecorded);
    }

    private string ResolvePath(Guid id) => Path.Combine(_sampleDirectory, id.ToString("N") + ".jsonl");

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
