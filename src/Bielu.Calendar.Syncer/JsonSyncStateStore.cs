using Microsoft.Extensions.Options;

namespace Bielu.Calendar.Syncer;

internal sealed class JsonSyncStateStore : IDisposable, ISyncStateStore
{
    private readonly JsonFileStore<List<SyncLink>> _store;

    public JsonSyncStateStore(IOptions<CalendarSyncerOptions> options) =>
        _store = new JsonFileStore<List<SyncLink>>(
            Path.Combine(options.Value.DataDirectory, "links.json"),
            () => []);

    public void Dispose() => _store.Dispose();

    public async Task<IReadOnlyList<SyncLink>> GetLinksAsync(
        Guid sourceAccountId,
        Guid targetAccountId,
        CancellationToken cancellationToken) =>
        await _store.ReadAsync(
            links => (IReadOnlyList<SyncLink>)links
                .Where(link => link.SourceAccountId == sourceAccountId && link.TargetAccountId == targetAccountId)
                .ToList(),
            cancellationToken);

    public async Task<IReadOnlyList<SyncLink>> GetLinksForAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        await _store.ReadAsync(
            links => (IReadOnlyList<SyncLink>)links
                .Where(link => link.SourceAccountId == accountId || link.TargetAccountId == accountId)
                .ToList(),
            cancellationToken);

    public Task SaveLinksAsync(IReadOnlyCollection<SyncLink> links, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(links);

        return links.Count == 0
            ? Task.CompletedTask
            : _store.MutateAsync(
                stored =>
                {
                    var incoming = links.Select(LinkKey).ToHashSet();
                    stored.RemoveAll(existing => incoming.Contains(LinkKey(existing)));
                    stored.AddRange(links);
                },
                cancellationToken);
    }

    public Task RemoveLinksAsync(IReadOnlyCollection<SyncLink> links, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(links);

        return links.Count == 0
            ? Task.CompletedTask
            : _store.MutateAsync(
                stored =>
                {
                    var doomed = links.Select(LinkKey).ToHashSet();
                    stored.RemoveAll(existing => doomed.Contains(LinkKey(existing)));
                },
                cancellationToken);
    }

    public Task RemoveLinksForAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        _store.MutateAsync(
            links => links.RemoveAll(link => link.SourceAccountId == accountId || link.TargetAccountId == accountId),
            cancellationToken);

    /// <summary>A link is uniquely identified by the source event and the calendar it is mirrored into.</summary>
    private static (Guid Source, Guid Target, string EventId) LinkKey(SyncLink link) =>
        (link.SourceAccountId, link.TargetAccountId, link.SourceEventId);
}
