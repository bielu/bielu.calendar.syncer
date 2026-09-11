namespace Bielu.Calendar.Syncer;

/// <summary>Reads and writes calendar entries for one backing calendar service.</summary>
public interface ICalendarProvider
{
    /// <summary>Stable key used to resolve this provider from DI, for example <c>google</c>.</summary>
    string ProviderName { get; }

    Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        CalendarAccount account,
        SyncWindow window,
        CancellationToken cancellationToken);

    Task<string> CreateEventAsync(CalendarAccount account, CalendarEvent calendarEvent, CancellationToken cancellationToken);

    Task UpdateEventAsync(CalendarAccount account, string eventId, CalendarEvent calendarEvent, CancellationToken cancellationToken);

    Task DeleteEventAsync(CalendarAccount account, string eventId, CancellationToken cancellationToken);
}

/// <summary>The span of time a sync run considers.</summary>
public readonly record struct SyncWindow(DateTimeOffset Start, DateTimeOffset End)
{
    public bool Contains(DateTimeOffset instant) => instant >= Start && instant <= End;
}
