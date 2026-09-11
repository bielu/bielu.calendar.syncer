namespace Bielu.Calendar.Syncer;

internal sealed class SyncStatusTracker : ISyncStatusTracker
{
    private const int HistoryLimit = 20;

    private readonly Lock _lock = new();
    private readonly LinkedList<SyncRunResult> _history = new();
    private int _running;

    public bool IsRunning => Volatile.Read(ref _running) > 0;

    public SyncRunResult? LastRun
    {
        get
        {
            lock (_lock)
            {
                return _history.First?.Value;
            }
        }
    }

    public DateTimeOffset? NextRunAt { get; set; }

    public IReadOnlyList<SyncRunResult> History
    {
        get
        {
            lock (_lock)
            {
                return [.. _history];
            }
        }
    }

    public IDisposable BeginRun()
    {
        Interlocked.Increment(ref _running);
        return new RunScope(this);
    }

    public void Record(SyncRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        lock (_lock)
        {
            _history.AddFirst(result);
            while (_history.Count > HistoryLimit)
            {
                _history.RemoveLast();
            }
        }
    }

    private sealed class RunScope(SyncStatusTracker tracker) : IDisposable
    {
        public void Dispose() => Interlocked.Decrement(ref tracker._running);
    }
}
