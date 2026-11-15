using UniversalITMonitor.Core.Persistence;

namespace UniversalITMonitor.Application.Backups;

public sealed class ApplicationMaintenanceLock : IApplicationMaintenanceLock
{
    private readonly object _sync = new();
    private TaskCompletionSource _changed = NewSignal();
    private int _readers;
    private int _waitingWriters;
    private bool _writerActive;

    public async ValueTask<IAsyncDisposable> EnterReadAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            Task waitTask;
            lock (_sync)
            {
                if (!_writerActive && _waitingWriters == 0)
                {
                    _readers++;
                    return new Lease(ReleaseRead);
                }

                waitTask = _changed.Task;
            }

            await waitTask.WaitAsync(cancellationToken);
        }
    }

    public async ValueTask<IAsyncDisposable> EnterWriteAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync) _waitingWriters++;
        try
        {
            while (true)
            {
                Task waitTask;
                lock (_sync)
                {
                    if (!_writerActive && _readers == 0)
                    {
                        _waitingWriters--;
                        _writerActive = true;
                        return new Lease(ReleaseWrite);
                    }

                    waitTask = _changed.Task;
                }

                await waitTask.WaitAsync(cancellationToken);
            }
        }
        catch
        {
            lock (_sync)
            {
                _waitingWriters--;
                Pulse();
            }
            throw;
        }
    }

    private void ReleaseRead()
    {
        lock (_sync)
        {
            _readers--;
            Pulse();
        }
    }

    private void ReleaseWrite()
    {
        lock (_sync)
        {
            _writerActive = false;
            Pulse();
        }
    }

    private void Pulse()
    {
        var previous = _changed;
        _changed = NewSignal();
        previous.TrySetResult();
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Lease(Action release) : IAsyncDisposable
    {
        private Action? _release = release;

        public ValueTask DisposeAsync()
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();
            return ValueTask.CompletedTask;
        }
    }
}
