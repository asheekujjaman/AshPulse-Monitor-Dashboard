using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Storage.Json;

public sealed class FileJsonDocumentStore : IJsonDocumentStore
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> LocalLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _dataDirectory;

    public FileJsonDocumentStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        _dataDirectory = Path.GetFullPath(dataDirectory);
        Directory.CreateDirectory(_dataDirectory);
    }

    public Task<TDocument> ReadAsync<TDocument>(
        string fileName,
        Func<TDocument> defaultFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(defaultFactory);
        var path = ResolvePath(fileName);

        return WithLockAsync(path, async () =>
        {
            return await ReadOrDefaultAsync(path, fileName, defaultFactory, cancellationToken);
        }, cancellationToken);
    }

    public Task EnsureAsync<TDocument>(
        string fileName,
        TDocument defaultDocument,
        CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(fileName);

        return WithLockAsync(path, async () =>
        {
            if (!File.Exists(path))
            {
                if (File.Exists(path + ".bak"))
                {
                    _ = await ReadOrDefaultAsync(path, fileName, () => defaultDocument, cancellationToken);
                }
                else
                {
                    await WriteUnlockedAsync(path, defaultDocument, createBackup: false, cancellationToken);
                }
            }

            return true;
        }, cancellationToken);
    }

    public Task WriteAsync<TDocument>(
        string fileName,
        TDocument document,
        CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(fileName);

        return WithLockAsync(path, async () =>
        {
            await WriteUnlockedAsync(path, document, createBackup: true, cancellationToken);
            return true;
        }, cancellationToken);
    }

    public Task<TResult> UpdateAsync<TDocument, TResult>(
        string fileName,
        Func<TDocument> defaultFactory,
        Func<TDocument, (TDocument Document, TResult Result)> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(defaultFactory);
        ArgumentNullException.ThrowIfNull(update);
        var path = ResolvePath(fileName);

        return WithLockAsync(path, async () =>
        {
            var current = await ReadOrDefaultAsync(path, fileName, defaultFactory, cancellationToken);
            var (document, result) = update(current);
            await WriteUnlockedAsync(path, document, createBackup: File.Exists(path), cancellationToken);
            return result;
        }, cancellationToken);
    }

    private async Task<T> WithLockAsync<T>(
        string path,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        var semaphore = LocalLocks.GetOrAdd(path, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            await using var fileLock = await AcquireProcessLockAsync(path + ".lock", cancellationToken);
            return await action();
        }
        finally
        {
            semaphore.Release();
        }
    }

    private static async Task<FileStream> AcquireProcessLockAsync(
        string lockPath,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    options: FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            }
        }
    }

    private async Task<TDocument> ReadExistingAsync<TDocument>(
        string path,
        string fileName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await DeserializeAsync<TDocument>(path, cancellationToken);
        }
        catch (JsonException primaryError)
        {
            var backupPath = path + ".bak";
            if (!File.Exists(backupPath))
            {
                throw new InvalidDataException(
                    $"The JSON data file '{fileName}' is invalid and no recovery backup is available.",
                    primaryError);
            }

            TDocument recovered;
            try
            {
                recovered = await DeserializeAsync<TDocument>(backupPath, cancellationToken);
            }
            catch (JsonException backupError)
            {
                throw new InvalidDataException(
                    $"The JSON data file '{fileName}' and its recovery backup are invalid. Both files were preserved.",
                    new AggregateException(primaryError, backupError));
            }

            var corruptPath = path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
            File.Move(path, corruptPath);
            await WriteUnlockedAsync(path, recovered, createBackup: false, cancellationToken);
            return recovered;
        }
    }

    private async Task<TDocument> ReadOrDefaultAsync<TDocument>(
        string path,
        string fileName,
        Func<TDocument> defaultFactory,
        CancellationToken cancellationToken)
    {
        if (File.Exists(path)) return await ReadExistingAsync<TDocument>(path, fileName, cancellationToken);

        var backupPath = path + ".bak";
        if (!File.Exists(backupPath)) return defaultFactory();

        TDocument recovered;
        try
        {
            recovered = await DeserializeAsync<TDocument>(backupPath, cancellationToken);
        }
        catch (JsonException backupError)
        {
            throw new InvalidDataException(
                $"The JSON data file '{fileName}' is missing and its recovery backup is invalid. The backup was preserved.",
                backupError);
        }

        await WriteUnlockedAsync(path, recovered, createBackup: false, cancellationToken);
        return recovered;
    }

    private static async Task<TDocument> DeserializeAsync<TDocument>(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        var document = await JsonSerializer.DeserializeAsync<TDocument>(stream, JsonOptions, cancellationToken);
        return document ?? throw new JsonException("The JSON document is null.");
    }

    private async Task WriteUnlockedAsync<TDocument>(
        string path,
        TDocument document,
        bool createBackup,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(payload, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            if (createBackup && File.Exists(path))
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
    }

    private string ResolvePath(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (!string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
            !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("Only simple JSON file names are allowed.", nameof(fileName));
        }

        return Path.Combine(_dataDirectory, fileName);
    }
}
