namespace UniversalITMonitor.Web.Infrastructure;

public sealed class ApplicationInstanceLease : IAsyncDisposable
{
    private readonly FileStream _lease;

    private ApplicationInstanceLease(FileStream lease) => _lease = lease;

    public static async Task<ApplicationInstanceLease> AcquireAsync(
        string dataDirectory,
        CancellationToken cancellationToken = default)
    {
        var fullDataDirectory = Path.GetFullPath(dataDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var leasePath = fullDataDirectory + ".instance.lock";
        var parent = Path.GetDirectoryName(leasePath)
            ?? throw new InvalidOperationException("The application data directory must have a parent directory.");
        Directory.CreateDirectory(parent);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var lease = new FileStream(leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.WriteThrough);
                lease.SetLength(0);
                var owner = System.Text.Encoding.UTF8.GetBytes($"pid={Environment.ProcessId};startedUtc={DateTimeOffset.UtcNow:O}");
                lease.Write(owner);
                lease.Flush(flushToDisk: true);
                return new ApplicationInstanceLease(lease);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
        }
    }

    public ValueTask DisposeAsync() => _lease.DisposeAsync();
}
