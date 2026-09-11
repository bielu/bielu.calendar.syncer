using System.Globalization;
using System.Text.Json.Serialization;

namespace Bielu.Calendar.Syncer.Microsoft;

internal sealed class MicrosoftCalendarProvider(IHttpClientFactory httpClientFactory)
    : RestCalendarProvider(httpClientFactory)
{
    public const string Name = "microsoft";

    private const string GraphRoot = "https://graph.microsoft.com/v1.0/me";

    public override string ProviderName => Name;

    public override async Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        CalendarAccount account,
        SyncWindow window,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        // calendarView expands recurring series into individual occurrences, matching what Google returns.
        var next = new Uri(
            $"{CalendarRoot(account)}/calendarView" +
            $"?startDateTime={Uri.EscapeDataString(window.Start.UtcDateTime.ToString("o", CultureInfo.InvariantCulture))}" +
            $"&endDateTime={Uri.EscapeDataString(window.End.UtcDateTime.ToString("o", CultureInfo.InvariantCulture))}" +
            "&$top=500");

        var events = new List<CalendarEvent>();
        while (true)
        {
            var page = await SendAsync<GraphEventList>(account, HttpMethod.Get, next, payload: null, cancellationToken);
            events.AddRange(page.Value.Select(ToCalendarEvent));

            if (string.IsNullOrEmpty(page.NextLink))
            {
                return events;
            }

            next = new Uri(page.NextLink);
        }
    }

    public override async Task<string> CreateEventAsync(
        CalendarAccount account,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken)
    {
        var created = await SendAsync<GraphEvent>(
            account,
            HttpMethod.Post,
            new Uri($"{CalendarRoot(account)}/events"),
            ToGraphEvent(calendarEvent),
            cancellationToken);

        return created.Id ?? throw new InvalidOperationException("Graph did not return an id for the created event.");
    }

    public override Task UpdateEventAsync(
        CalendarAccount account,
        string eventId,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken) =>
        SendIgnoringMissingAsync(
            account,
            HttpMethod.Patch,
            new Uri($"{GraphRoot}/events/{Uri.EscapeDataString(eventId)}"),
            ToGraphEvent(calendarEvent),
            cancellationToken);

    public override Task DeleteEventAsync(CalendarAccount account, string eventId, CancellationToken cancellationToken) =>
        SendIgnoringMissingAsync(
            account,
            HttpMethod.Delete,
            new Uri($"{GraphRoot}/events/{Uri.EscapeDataString(eventId)}"),
            payload: null,
            cancellationToken);

    protected override void ConfigureRequest(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Without this Graph renders times in the mailbox's own zone, which would shift every mirrored event.
        request.Headers.TryAddWithoutValidation("Prefer", "outlook.timezone=\"UTC\"");
    }

    private static string CalendarRoot(CalendarAccount account) =>
        account.CalendarId is { Length: > 0 } calendarId
            ? $"{GraphRoot}/calendars/{Uri.EscapeDataString(calendarId)}"
            : GraphRoot;

    private static CalendarEvent ToCalendarEvent(GraphEvent source)
    {
        var body = source.Body?.Content;

        return new CalendarEvent
        {
            Id = source.Id ?? string.Empty,
            Subject = source.Subject ?? "(no title)",
            Body = body,
            Location = source.Location?.DisplayName,
            Start = ToDateTimeOffset(source.Start),
            End = ToDateTimeOffset(source.End),
            IsAllDay = source.IsAllDay,
            IsPrivate = source.Sensitivity is "private" or "confidential",
            Availability = ToAvailability(source.ShowAs),
            LastModified = source.LastModifiedDateTime,
            Marker = MirrorMarker.TryParse(body),
        };
    }

    private static GraphEvent ToGraphEvent(CalendarEvent source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new GraphEvent
        {
            Subject = source.Subject,
            Body = new GraphBody { ContentType = "text", Content = source.Body ?? string.Empty },
            Location = source.Location is null ? null : new GraphLocation { DisplayName = source.Location },
            Start = ToGraphDate(source.Start),
            End = ToGraphDate(source.End),
            IsAllDay = source.IsAllDay,
            Sensitivity = source.IsPrivate ? "private" : "normal",
            ShowAs = ToShowAs(source.Availability),
        };
    }

    private static EventAvailability ToAvailability(string? showAs) => showAs switch
    {
        "free" => EventAvailability.Free,
        "tentative" => EventAvailability.Tentative,
        "oof" => EventAvailability.OutOfOffice,
        "workingElsewhere" => EventAvailability.WorkingElsewhere,
        _ => EventAvailability.Busy,
    };

    private static string ToShowAs(EventAvailability availability) => availability switch
    {
        EventAvailability.Free => "free",
        EventAvailability.Tentative => "tentative",
        EventAvailability.OutOfOffice => "oof",
        EventAvailability.WorkingElsewhere => "workingElsewhere",
        _ => "busy",
    };

    private static GraphDate ToGraphDate(DateTimeOffset instant) =>
        new()
        {
            DateTime = instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture),
            TimeZone = "UTC",
        };

    private static DateTimeOffset ToDateTimeOffset(GraphDate? date)
    {
        if (date?.DateTime is not { Length: > 0 } value)
        {
            return DateTimeOffset.MinValue;
        }

        // Graph returns an unzoned stamp plus a separate timeZone field; the Prefer header pins that to UTC.
        var parsed = System.DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        return new DateTimeOffset(System.DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
    }

    private sealed record GraphEventList
    {
        public IReadOnlyList<GraphEvent> Value { get; init; } = [];

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; init; }
    }

    private sealed record GraphEvent
    {
        /// <summary>Server-assigned; left null on writes so it is omitted from the payload.</summary>
        public string? Id { get; init; }
        public string? Subject { get; init; }
        public GraphBody? Body { get; init; }
        public GraphLocation? Location { get; init; }
        public GraphDate? Start { get; init; }
        public GraphDate? End { get; init; }
        public bool IsAllDay { get; init; }
        public string? Sensitivity { get; init; }
        public string? ShowAs { get; init; }
        public DateTimeOffset? LastModifiedDateTime { get; init; }
    }

    private sealed record GraphBody
    {
        public string ContentType { get; init; } = "text";
        public string Content { get; init; } = string.Empty;
    }

    private sealed record GraphLocation
    {
        public string DisplayName { get; init; } = string.Empty;
    }

    private sealed record GraphDate
    {
        public string DateTime { get; init; } = string.Empty;
        public string TimeZone { get; init; } = "UTC";
    }
}
