namespace Bielu.Calendar.Syncer;

/// <summary>Keeps the recent sync history in memory so the dashboard has something to show.</summary>
public interface ISyncStatusTracker
{
    bool IsRunning { get; }

    SyncRunResult? LastRun { get; }

    DateTimeOffset? NextRunAt { get; set; }

    IReadOnlyList<SyncRunResult> History { get; }

    IDisposable BeginRun();

    void Record(SyncRunResult result);
}
