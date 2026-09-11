namespace Bielu.Calendar.Syncer;

/// <summary>A single connected calendar, identified by the provider that owns it and the OAuth tokens it holds.</summary>
public sealed record CalendarAccount
{
    public required Guid Id { get; init; }

    /// <summary>Provider key, matching <see cref="ICalendarProvider.ProviderName"/> (for example <c>google</c>).</summary>
    public required string ProviderName { get; init; }

    /// <summary>Address of the signed-in mailbox, shown on the dashboard.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Calendar to sync; <c>null</c> means the account's default calendar.</summary>
    public string? CalendarId { get; init; }

    public required string RefreshToken { get; init; }
    public string? AccessToken { get; init; }
    public DateTimeOffset AccessTokenExpiresAt { get; init; }

    public bool Enabled { get; init; } = true;
    public DateTimeOffset ConnectedAt { get; init; }

    public CalendarAccount WithTokens(string accessToken, DateTimeOffset expiresAt, string? refreshToken = null) =>
        this with
        {
            AccessToken = accessToken,
            AccessTokenExpiresAt = expiresAt,
            RefreshToken = string.IsNullOrEmpty(refreshToken) ? RefreshToken : refreshToken,
        };
}
