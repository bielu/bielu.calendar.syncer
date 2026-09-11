namespace Bielu.Calendar.Syncer;

/// <summary>How an event affects free/busy lookups.</summary>
public enum EventAvailability
{
    Free,
    Tentative,
    Busy,
    OutOfOffice,
    WorkingElsewhere,
}
