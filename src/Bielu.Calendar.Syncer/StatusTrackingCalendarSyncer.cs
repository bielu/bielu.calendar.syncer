namespace Bielu.Calendar.Syncer;

/// <summary>Decorates the syncer so every run, however it was triggered, lands in the dashboard's history.</summary>
internal sealed class StatusTrackingCalendarSyncer(ICalendarSyncer inner, ISyncStatusTracker tracker) : ICalendarSyncer
{
    public async Task<SyncRunResult> SyncAsync(CancellationToken cancellationToken)
    {
        using var _ = tracker.BeginRun();
        var result = await inner.SyncAsync(cancellationToken);
        tracker.Record(result);
        return result;
    }
}
