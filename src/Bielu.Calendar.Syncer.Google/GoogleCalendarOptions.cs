namespace Bielu.Calendar.Syncer.Google;

public sealed class GoogleCalendarOptions
{
    public const string SectionName = "CalendarSyncer:Google";

    /// <summary>OAuth client id from a Google Cloud "Desktop app" credential.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret issued alongside the desktop client id. Google requires it on the token exchange even for
    /// installed apps, where it is not treated as a confidential credential.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;
}
