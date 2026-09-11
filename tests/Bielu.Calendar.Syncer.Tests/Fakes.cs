namespace Bielu.Calendar.Syncer.Tests;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class InMemoryAccountStore(params CalendarAccount[] accounts) : IAccountStore
{
    private readonly List<CalendarAccount> _accounts = [.. accounts];

    public Task<IReadOnlyList<CalendarAccount>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CalendarAccount>>([.. _accounts]);

    public Task<CalendarAccount?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_accounts.FirstOrDefault(account => account.Id == id));

    public Task SaveAsync(CalendarAccount account, CancellationToken cancellationToken)
    {
        _accounts.RemoveAll(existing => existing.Id == account.Id);
        _accounts.Add(account);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(Guid id, CancellationToken cancellationToken)
    {
        _accounts.RemoveAll(account => account.Id == id);
        return Task.CompletedTask;
    }
}

internal sealed class InMemorySyncStateStore : ISyncStateStore
{
    private readonly List<SyncLink> _links = [];

    public IReadOnlyList<SyncLink> All => _links;

    public Task<IReadOnlyList<SyncLink>> GetLinksAsync(Guid sourceAccountId, Guid targetAccountId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SyncLink>>(
            [.. _links.Where(link => link.SourceAccountId == sourceAccountId && link.TargetAccountId == targetAccountId)]);

    public Task<IReadOnlyList<SyncLink>> GetLinksForAccountAsync(Guid accountId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SyncLink>>(
            [.. _links.Where(link => link.SourceAccountId == accountId || link.TargetAccountId == accountId)]);

    public Task SaveLinksAsync(IReadOnlyCollection<SyncLink> links, CancellationToken cancellationToken)
    {
        foreach (var link in links)
        {
            _links.RemoveAll(existing => Key(existing) == Key(link));
            _links.Add(link);
        }

        return Task.CompletedTask;
    }

    public Task RemoveLinksAsync(IReadOnlyCollection<SyncLink> links, CancellationToken cancellationToken)
    {
        foreach (var link in links)
        {
            _links.RemoveAll(existing => Key(existing) == Key(link));
        }

        return Task.CompletedTask;
    }

    public Task RemoveLinksForAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        _links.RemoveAll(link => link.SourceAccountId == accountId || link.TargetAccountId == accountId);
        return Task.CompletedTask;
    }

    private static (Guid, Guid, string) Key(SyncLink link) =>
        (link.SourceAccountId, link.TargetAccountId, link.SourceEventId);
}

/// <summary>
/// Stands in for a real calendar backend. Like the real providers it parses the mirror marker out of the body
/// on read, which is what the loop-prevention tests depend on.
/// </summary>
internal sealed class FakeCalendarProvider : ICalendarProvider
{
    private readonly Dictionary<Guid, List<CalendarEvent>> _calendars = [];
    private int _nextId;

    public string ProviderName => "fake";

    public List<CalendarEvent> Calendar(Guid accountId) =>
        _calendars.TryGetValue(accountId, out var events) ? events : _calendars[accountId] = [];

    public Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        CalendarAccount account,
        SyncWindow window,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CalendarEvent>>(
        [
            .. Calendar(account.Id)
                .Where(calendarEvent => window.Contains(calendarEvent.Start))
                .Select(calendarEvent => calendarEvent with { Marker = MirrorMarker.TryParse(calendarEvent.Body) }),
        ]);

    public Task<string> CreateEventAsync(CalendarAccount account, CalendarEvent calendarEvent, CancellationToken cancellationToken)
    {
        var id = $"evt-{++_nextId}";
        Calendar(account.Id).Add(calendarEvent with { Id = id });
        return Task.FromResult(id);
    }

    public Task UpdateEventAsync(CalendarAccount account, string eventId, CalendarEvent calendarEvent, CancellationToken cancellationToken)
    {
        var events = Calendar(account.Id);
        var index = events.FindIndex(existing => existing.Id == eventId);
        if (index >= 0)
        {
            events[index] = calendarEvent with { Id = eventId };
        }

        return Task.CompletedTask;
    }

    public Task DeleteEventAsync(CalendarAccount account, string eventId, CancellationToken cancellationToken)
    {
        Calendar(account.Id).RemoveAll(existing => existing.Id == eventId);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAuthenticator : ICalendarAuthenticator
{
    public string ProviderName => "fake";

    public bool IsConfigured => true;

    public Uri BuildAuthorizationUrl(AuthorizationRequest request) => new("https://example.invalid/authorize");

    public Task<CalendarAccount> CompleteAuthorizationAsync(AuthorizationCallback callback, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Connecting accounts is not exercised by these tests.");

    public Task<CalendarAccount> EnsureAccessTokenAsync(CalendarAccount account, CancellationToken cancellationToken) =>
        Task.FromResult(account);
}
