namespace Bielu.Calendar.Syncer;

/// <summary>Provider-agnostic representation of a single calendar entry.</summary>
public sealed record CalendarEvent
{
    public required string Id { get; init; }
    public required string Subject { get; init; }
    public string? Body { get; init; }
    public string? Location { get; init; }
    public required DateTimeOffset Start { get; init; }
    public required DateTimeOffset End { get; init; }
    public bool IsAllDay { get; init; }

    /// <summary>Hides the event's details from anyone but the owner. Maps to Google visibility and Graph sensitivity.</summary>
    public bool IsPrivate { get; init; }

    /// <summary>Whether the event occupies the owner's time. Maps to Google transparency and Graph showAs.</summary>
    public EventAvailability Availability { get; init; } = EventAvailability.Busy;

    public DateTimeOffset? LastModified { get; init; }

    /// <summary>Set when this event is itself a mirror of an event on another account.</summary>
    public MirrorMarker? Marker { get; init; }
}
