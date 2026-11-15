using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace UniversalITMonitor.Web.Logging;

public sealed class RollingRedactedFileLoggerProvider : ILoggerProvider
{
    private const int MaximumMessageCharacters = 16_384;
    private const int MaximumExceptionCharacters = 8_192;
    private static readonly Regex SecretAssignment = new(
        "(?i)(?<prefix>[\\\"']?(?:password|token|secret|community|string|authorization|credential|api[_-]?key|private[_-]?key)[\\\"']?\\s*[:=]\\s*[\\\"']?)(?<value>[^\\\"'&;,\\s}]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex TelegramBotToken = new("(?i)bot\\d+:[a-z0-9_-]{20,}", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly long _maximumFileBytes;
    private readonly int _retainedFiles;
    private StreamWriter? _writer;
    private string? _activePath;
    private DateOnly _activeDate;
    private int _activeSequence;
    private bool _disposed;

    public RollingRedactedFileLoggerProvider(string directory, long maximumFileBytes = 10 * 1024 * 1024, int retainedFiles = 30)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        _maximumFileBytes = Math.Clamp(maximumFileBytes, 64 * 1024, 256 * 1024 * 1024);
        _retainedFiles = Math.Clamp(retainedFiles, 1, 365);
        Directory.CreateDirectory(_directory);
    }

    public ILogger CreateLogger(string categoryName) => new RollingRedactedFileLogger(this, categoryName);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _writer?.Dispose();
            _writer = null;
        }
    }

    private bool IsEnabled(LogLevel level) => level != LogLevel.None;

    private void Write<TState>(
        string category,
        LogLevel level,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(level)) return;

        var message = formatter(state, exception) ?? string.Empty;
        if (state is IEnumerable<KeyValuePair<string, object?>> properties)
        {
            foreach (var property in properties)
            {
                if (property.Key.Equals("{OriginalFormat}", StringComparison.Ordinal)) continue;
                var sensitiveName = IsSensitiveProperty(property.Key);
                var value = property.Value?.ToString();
                if (sensitiveName && !string.IsNullOrEmpty(value))
                {
                    message = message.Replace(value, "[REDACTED]", StringComparison.Ordinal);
                }
            }
        }

        if (message.Length > MaximumMessageCharacters) message = message[..MaximumMessageCharacters] + " [truncated]";
        message = Redact(message);
        var safeException = exception is null
            ? null
            : $"{exception.GetType().Name}: {Redact(exception.Message.Length > MaximumExceptionCharacters ? exception.Message[..MaximumExceptionCharacters] + " [truncated]" : exception.Message)}";
        var line = JsonSerializer.Serialize(new LogRecord(
            DateTimeOffset.UtcNow,
            level.ToString(),
            category.Length > 512 ? category[..512] : category,
            eventId.Id,
            eventId.Name,
            message,
            safeException), JsonOptions);
        var bytes = Encoding.UTF8.GetByteCount(line) + 1;

        try
        {
            lock (_gate)
            {
                if (_disposed) return;
                EnsureWriter(bytes);
                _writer!.WriteLine(line);
                _writer.Flush();
                if (_writer.BaseStream is FileStream fileStream) fileStream.Flush(flushToDisk: true);
                _writer.Dispose();
                _writer = null;
                _activePath = null;
            }
        }
        catch (IOException) { ResetWriterAfterFailure(); }
        catch (UnauthorizedAccessException) { ResetWriterAfterFailure(); }
    }

    private void ResetWriterAfterFailure()
    {
        lock (_gate)
        {
            try { _writer?.Dispose(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            _writer = null;
            _activePath = null;
        }
    }

    private void EnsureWriter(int nextLineBytes)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (_writer is not null && today == _activeDate && CurrentLength() + nextLineBytes <= _maximumFileBytes) return;

        _writer?.Dispose();
        _writer = null;
        _activeDate = today;

        var files = Directory.EnumerateFiles(_directory, $"uim-{today:yyyyMMdd}-*.jsonl")
            .Select(path => (Path: path, Sequence: ParseSequence(path), Length: new FileInfo(path).Length))
            .OrderBy(item => item.Sequence)
            .ToArray();
        var last = files.LastOrDefault();
        _activeSequence = files.Length > 0 && last.Length + nextLineBytes <= _maximumFileBytes
            ? last.Sequence
            : files.Length == 0 ? 0 : last.Sequence + 1;
        _activePath = Path.Combine(_directory, $"uim-{today:yyyyMMdd}-{_activeSequence:D3}.jsonl");
        var stream = new FileStream(_activePath, FileMode.Append, FileAccess.Write, FileShare.Read | FileShare.Delete, 16 * 1024, FileOptions.SequentialScan);
        _writer = new StreamWriter(stream, new UTF8Encoding(false));
        PruneFiles();
    }

    private long CurrentLength() => _activePath is not null && File.Exists(_activePath) ? new FileInfo(_activePath).Length : 0;

    private void PruneFiles()
    {
        var files = Directory.EnumerateFiles(_directory, "uim-*.jsonl")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();
        foreach (var file in files.Skip(_retainedFiles))
        {
            if (string.Equals(file, _activePath, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
            try { File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static int ParseSequence(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var separator = name.LastIndexOf('-');
        return separator >= 0 && int.TryParse(name.AsSpan(separator + 1), out var sequence) ? sequence : 0;
    }

    private static bool IsSensitiveProperty(string name) =>
        name.Contains("password", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("token", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("community", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("privatekey", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("apikey", StringComparison.OrdinalIgnoreCase);

    private static string Redact(string value)
    {
        value = SecretAssignment.Replace(value, match => match.Groups["prefix"].Value + "[REDACTED]");
        return TelegramBotToken.Replace(value, "bot[REDACTED]");
    }

    private sealed record LogRecord(
        DateTimeOffset TimestampUtc,
        string Level,
        string Category,
        int EventId,
        string? EventName,
        string Message,
        string? Exception);

    private sealed class RollingRedactedFileLogger(RollingRedactedFileLoggerProvider owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => owner.IsEnabled(logLevel);
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            owner.Write(category, logLevel, eventId, state, exception, formatter);
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();
        public void Dispose() { }
    }
}
