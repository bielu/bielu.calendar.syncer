namespace Bielu.Calendar.Syncer;

/// <summary>Connects, pauses and disconnects calendar accounts, keeping mirrored events consistent as it does.</summary>
public interface IAccountManager
{
    Task<IReadOnlyList<CalendarAccount>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Starts a sign-in and returns the consent URL the browser should be sent to.</summary>
    Task<Uri> BeginConnectAsync(string providerName, Uri redirectUri, CancellationToken cancellationToken);

    /// <summary>Finishes a sign-in started by <see cref="BeginConnectAsync"/> and stores the connected account.</summary>
    Task<CalendarAccount> CompleteConnectAsync(string state, string code, Uri redirectUri, CancellationToken cancellationToken);

    Task SetEnabledAsync(Guid accountId, bool enabled, CancellationToken cancellationToken);

    /// <summary>Removes the account and deletes every mirrored copy it is involved in, in both directions.</summary>
    Task DisconnectAsync(Guid accountId, CancellationToken cancellationToken);
}
