namespace Bielu.Calendar.Syncer;

/// <summary>Mirrors events between every pair of enabled accounts.</summary>
public interface ICalendarSyncer
{
    Task<SyncRunResult> SyncAsync(CancellationToken cancellationToken);
}
