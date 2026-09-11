namespace Bielu.Calendar.Syncer;

public sealed class CalendarSyncerOptions
{
    public const string SectionName = "CalendarSyncer";

    /// <summary>How often the background worker runs a full sync.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How far back from now events are mirrored.</summary>
    public TimeSpan LookBehind { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How far ahead of now events are mirrored.</summary>
    public TimeSpan LookAhead { get; set; } = TimeSpan.FromDays(60);

    /// <summary>Directory holding accounts, sync links and other local state.</summary>
    public string DataDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bielu", "calendar-syncer");

    /// <summary>Prefix added to the subject of every mirrored event, so copies are obvious in the calendar UI.</summary>
    public string MirrorSubjectPrefix { get; set; } = string.Empty;
}
