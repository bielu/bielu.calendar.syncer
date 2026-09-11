namespace Bielu.Calendar.Syncer.Microsoft;

public sealed class MicrosoftCalendarOptions
{
    public const string SectionName = "CalendarSyncer:Microsoft";

    /// <summary>Application (client) id of an Entra ID app registration configured as a public client.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Directory the sign-in is issued against. <c>common</c> accepts both work/school and personal Microsoft
    /// accounts, which is what a mixed personal setup needs.
    /// </summary>
    public string Tenant { get; set; } = "common";
}
