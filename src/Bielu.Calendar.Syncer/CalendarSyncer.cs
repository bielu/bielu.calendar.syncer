using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bielu.Calendar.Syncer;

/// <summary>
/// Mirrors events across every ordered pair of enabled accounts. Only events that are not themselves mirrors are
/// treated as sources, which is what keeps a copy from being copied back to where it came from.
/// </summary>
internal sealed partial class CalendarSyncer(
    IAccountStore accountStore,
    ISyncStateStore stateStore,
    ICalendarProviderRegistry registry,
    IOptions<CalendarSyncerOptions> options,
    TimeProvider timeProvider,
    ILogger<CalendarSyncer> logger) : ICalendarSyncer, IDisposable
{
    private readonly CalendarSyncerOptions _options = options.Value;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Dispose() => _gate.Dispose();

    public async Task<SyncRunResult> SyncAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await RunAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<SyncRunResult> RunAsync(CancellationToken cancellationToken)
    {
        var startedAt = timeProvider.GetUtcNow();
        var window = new SyncWindow(startedAt - _options.LookBehind, startedAt + _options.LookAhead);

        var errors = new List<string>();
        var accounts = (await accountStore.GetAllAsync(cancellationToken)).Where(account => account.Enabled).ToList();

        var snapshots = new List<AccountSnapshot>();
        foreach (var account in accounts)
        {
            try
            {
                snapshots.Add(await LoadSnapshotAsync(account, window, cancellationToken));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogAccountReadFailed(exception, account.DisplayName);
                errors.Add($"{account.DisplayName}: {exception.Message}");
            }
        }

        var totals = new PairResult(0, 0, 0);
        foreach (var source in snapshots)
        {
            foreach (var target in snapshots)
            {
                if (source.Account.Id == target.Account.Id)
                {
                    continue;
                }

                try
                {
                    totals += await MirrorAsync(source, target, window, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogMirrorFailed(exception, source.Account.DisplayName, target.Account.DisplayName);
                    errors.Add($"{source.Account.DisplayName} -> {target.Account.DisplayName}: {exception.Message}");
                }
            }
        }

        var result = new SyncRunResult
        {
            StartedAt = startedAt,
            CompletedAt = timeProvider.GetUtcNow(),
            Created = totals.Created,
            Updated = totals.Updated,
            Deleted = totals.Deleted,
            AccountsSynced = snapshots.Count,
            Errors = errors,
        };

        LogSyncFinished(
            result.Duration,
            result.AccountsSynced,
            result.Created,
            result.Updated,
            result.Deleted,
            errors.Count);

        return result;
    }

    private async Task<AccountSnapshot> LoadSnapshotAsync(
        CalendarAccount account,
        SyncWindow window,
        CancellationToken cancellationToken)
    {
        var authenticator = registry.GetAuthenticator(account.ProviderName);
        var refreshed = await authenticator.EnsureAccessTokenAsync(account, cancellationToken);
        if (!ReferenceEquals(refreshed, account))
        {
            await accountStore.SaveAsync(refreshed, cancellationToken);
        }

        var provider = registry.GetProvider(refreshed.ProviderName);
        var events = await provider.GetEventsAsync(refreshed, window, cancellationToken);
        return new AccountSnapshot(refreshed, events);
    }

    private async Task<PairResult> MirrorAsync(
        AccountSnapshot source,
        AccountSnapshot target,
        SyncWindow window,
        CancellationToken cancellationToken)
    {
        var provider = registry.GetProvider(target.Account.ProviderName);
        var originals = source.Events.Where(calendarEvent => calendarEvent.Marker is null).ToList();
        var links = await stateStore.GetLinksAsync(source.Account.Id, target.Account.Id, cancellationToken);
        var linksBySourceEvent = links.ToDictionary(link => link.SourceEventId, StringComparer.Ordinal);
        var targetEventIds = target.Events.Select(calendarEvent => calendarEvent.Id).ToHashSet(StringComparer.Ordinal);

        var created = 0;
        var updated = 0;
        var touchedLinks = new List<SyncLink>();
        var staleLinks = new List<SyncLink>();

        foreach (var original in originals)
        {
            var mirror = BuildMirror(original, source.Account.Id);

            if (linksBySourceEvent.TryGetValue(original.Id, out var link) && targetEventIds.Contains(link.TargetEventId))
            {
                if (link.SourceLastModified is not null &&
                    original.LastModified is not null &&
                    original.LastModified <= link.SourceLastModified)
                {
                    continue;
                }

                await provider.UpdateEventAsync(target.Account, link.TargetEventId, mirror, cancellationToken);
                touchedLinks.Add(link with { SourceLastModified = original.LastModified, SourceStart = original.Start });
                updated++;
                continue;
            }

            var targetEventId = await provider.CreateEventAsync(target.Account, mirror, cancellationToken);
            touchedLinks.Add(
                new SyncLink
                {
                    SourceAccountId = source.Account.Id,
                    SourceEventId = original.Id,
                    TargetAccountId = target.Account.Id,
                    TargetEventId = targetEventId,
                    SourceLastModified = original.LastModified,
                    SourceStart = original.Start,
                });
            created++;
        }

        var liveSourceEventIds = originals.Select(calendarEvent => calendarEvent.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var link in links)
        {
            // A link whose source event sits outside the window was never fetched, so it is not a deletion.
            if (liveSourceEventIds.Contains(link.SourceEventId) || !window.Contains(link.SourceStart))
            {
                continue;
            }

            await DeleteMirrorAsync(provider, target.Account, link, cancellationToken);
            staleLinks.Add(link);
        }

        // Persisted once per pair rather than per event; every save rewrites the whole state file.
        await stateStore.SaveLinksAsync(touchedLinks, cancellationToken);
        await stateStore.RemoveLinksAsync(staleLinks, cancellationToken);

        return new PairResult(created, updated, staleLinks.Count);
    }

    private async Task DeleteMirrorAsync(
        ICalendarProvider provider,
        CalendarAccount targetAccount,
        SyncLink link,
        CancellationToken cancellationToken)
    {
        try
        {
            await provider.DeleteEventAsync(targetAccount, link.TargetEventId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The link is dropped either way: a mirror we cannot delete is one we should stop tracking.
            LogMirrorDeleteFailed(exception, link.TargetEventId, targetAccount.DisplayName);
        }
    }

    private CalendarEvent BuildMirror(CalendarEvent original, Guid sourceAccountId)
    {
        var marker = new MirrorMarker(sourceAccountId, original.Id);
        var body = string.IsNullOrWhiteSpace(original.Body)
            ? marker.ToBodyTag()
            : $"{original.Body}\n\n{marker.ToBodyTag()}";

        return original with
        {
            Id = string.Empty,
            Subject = _options.MirrorSubjectPrefix + original.Subject,
            Body = body,
            Marker = marker,
        };
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to read events for {Account}")]
    private partial void LogAccountReadFailed(Exception exception, string account);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to mirror {Source} into {Target}")]
    private partial void LogMirrorFailed(Exception exception, string source, string target);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Could not delete mirrored event {EventId} on {Account}; dropping the link anyway")]
    private partial void LogMirrorDeleteFailed(Exception exception, string eventId, string account);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Sync finished in {Duration} across {Accounts} accounts: {Created} created, {Updated} updated, {Deleted} deleted, {Errors} errors")]
    private partial void LogSyncFinished(
        TimeSpan duration,
        int accounts,
        int created,
        int updated,
        int deleted,
        int errors);

    private sealed record AccountSnapshot(CalendarAccount Account, IReadOnlyList<CalendarEvent> Events);

    private readonly record struct PairResult(int Created, int Updated, int Deleted)
    {
        public static PairResult operator +(PairResult left, PairResult right) =>
            new(left.Created + right.Created, left.Updated + right.Updated, left.Deleted + right.Deleted);
    }
}
