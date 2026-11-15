namespace UniversalITMonitor.Core.Persistence;

public interface IApplicationMaintenanceLock
{
    ValueTask<IAsyncDisposable> EnterReadAsync(CancellationToken cancellationToken = default);

    ValueTask<IAsyncDisposable> EnterWriteAsync(CancellationToken cancellationToken = default);
}
