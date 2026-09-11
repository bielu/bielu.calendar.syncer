using System.Globalization;
using System.Text.Json.Serialization;

namespace Bielu.Calendar.Syncer.Google;

internal sealed class GoogleCalendarProvider(IHttpClientFactory httpClientFactory) : RestCalendarProvider(httpClientFactory)
{
    public const string Name = "google";

    private const string ApiRoot = "https://www.googleapis.com/calendar/v3/calendars";
    private const string DefaultCalendarId = "primary";

    public override string ProviderName => Name;

    public override async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        CalendarAccount account,
        SyncWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        var events = new List<CalendarEvent>();
        string? pageToken = null;

        do
        {
            var query =
                $"?singleEvents=true&showDeleted=false&maxResults=2500" +
                $"&timeMin={Uri.EscapeDataString(window.Start.ToString("o", CultureInfo.InvariantCulture))}" +
                $"&timeMax={Uri.EscapeDataString(window.End.ToString("o", CultureInfo.InvariantCulture))}" +
                (pageToken is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");

            var page = await SendAsync<GoogleEventList>(
                account,
                HttpMethod.Get,
                new Uri(EventsUri(account) + query),
                payload: null,
                cancellationToken);

            events.AddRange(page.Items.Select(ToCalendarEvent));
            pageToken = page.NextPageToken;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return events;
    }

    public override async Task<string> CreateEventAsync(
        CalendarAccount account,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken)
    {
        var created = await SendAsync<GoogleEvent>(
            account,
            HttpMethod.Post,
            new Uri(EventsUri(account)),
            ToGoogleEvent(calendarEvent),
            cancellationToken);

        return created.Id ?? throw new InvalidOperationException("Google did not return an id for the created event.");
    }

    public override Task UpdateEventAsync(
        CalendarAccount account,
        string eventId,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken) =>
        SendIgnoringMissingAsync(
            account,
            HttpMethod.Put,
            new Uri($"{EventsUri(account)}/{Uri.EscapeDataString(eventId)}"),
            ToGoogleEvent(calendarEvent),
            cancellationToken);

    public override Task DeleteEventAsync(CalendarAccount account, string eventId, CancellationToken cancellationToken) =>
        SendIgnoringMissingAsync(
            account,
            HttpMethod.Delete,
            new Uri($"{EventsUri(account)}/{Uri.EscapeDataString(eventId)}"),
            payload: null,
            cancellationToken);

    private static string EventsUri(CalendarAccount account) =>
        $"{ApiRoot}/{Uri.EscapeDataString(account.CalendarId ?? DefaultCalendarId)}/events";

    private static CalendarEvent ToCalendarEvent(GoogleEvent source) =>
        new()
        {
            Id = source.Id ?? string.Empty,
            Subject = source.Summary ?? "(no title)",
            Body = source.Description,
            Location = source.Location,
            Start = ToDateTimeOffset(source.Start),
            End = ToDateTimeOffset(source.End),
            IsAllDay = source.Start?.Date is not null,
            IsPrivate = source.Visibility is "private" or "confidential",
            Availability = string.Equals(source.Transparency, "transparent", StringComparison.Ordinal)
                ? EventAvailability.Free
                : EventAvailability.Busy,
            LastModified = source.Updated,
            Marker = MirrorMarker.TryParse(source.Description),
        };

    private static GoogleEvent ToGoogleEvent(CalendarEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new GoogleEvent
        {
            Summary = source.Subject,
            Description = source.Body,
            Location = source.Location,
            Start = ToGoogleDate(source.Start, source.IsAllDay),
            End = ToGoogleDate(source.End, source.IsAllDay),
            Visibility = source.IsPrivate ? "private" : "default",
            // Google only models busy versus free, so anything that occupies time collapses to busy.
            Transparency = source.Availability == EventAvailability.Free ? "transparent" : "opaque",
        };
    }

    private static GoogleEventDate ToGoogleDate(DateTimeOffset instant, bool isAllDay) =>
        isAllDay
            ? new GoogleEventDate { Date = instant.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
            : new GoogleEventDate { DateTime = instant.ToString("o", CultureInfo.InvariantCulture) };

    private static DateTimeOffset ToDateTimeOffset(GoogleEventDate? date)
    {
        if (date?.DateTime is { Length: > 0 } dateTime)
        {
            return DateTimeOffset.Parse(dateTime, CultureInfo.InvariantCulture);
        }

        return date?.Date is { Length: > 0 } dateOnly
            ? new DateTimeOffset(DateOnly.Parse(dateOnly, CultureInfo.InvariantCulture), TimeOnly.MinValue, TimeSpan.Zero)
            : DateTimeOffset.MinValue;
    }

    private sealed record GoogleEventList
    {
        public IReadOnlyList<GoogleEvent> Items { get; init; } = [];

        public string? NextPageToken { get; init; }
    }

    private sealed record GoogleEvent
    {
        /// <summary>Server-assigned; left null on writes so it is omitted from the payload.</summary>
        public string? Id { get; init; }
        public string? Summary { get; init; }
        public string? Description { get; init; }
        public string? Location { get; init; }
        public GoogleEventDate? Start { get; init; }
        public GoogleEventDate? End { get; init; }
        public string? Visibility { get; init; }
        public string? Transparency { get; init; }
        public DateTimeOffset? Updated { get; init; }
    }

    private sealed record GoogleEventDate
    {
        /// <summary>Set for timed events; RFC3339 with an offset.</summary>
        [JsonPropertyName("dateTime")]
        public string? DateTime { get; init; }

        /// <summary>Set for all-day events; the end date is exclusive.</summary>
        [JsonPropertyName("date")]
        public string? Date { get; init; }
    }
}
