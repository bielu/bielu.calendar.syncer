using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Bielu.Calendar.Syncer;

internal sealed partial class AccountManager(
    IAccountStore accountStore,
    ISyncStateStore stateStore,
    ICalendarProviderRegistry registry,
    TimeProvider timeProvider,
    ILogger<AccountManager> logger) : IAccountManager
{
    private static readonly TimeSpan PendingSignInLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, PendingSignIn> _pending = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<CalendarAccount>> ListAsync(CancellationToken cancellationToken) =>
        accountStore.GetAllAsync(cancellationToken);

    public Task<Uri> BeginConnectAsync(string providerName, Uri redirectUri, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentNullException.ThrowIfNull(redirectUri);

        var authenticator = registry.GetAuthenticator(providerName);
        if (!authenticator.IsConfigured)
        {
            throw new InvalidOperationException(
                $"The '{providerName}' provider has no client id configured, so it cannot start a sign-in.");
        }

        PrunePendingSignIns();

        var state = CreateRandomToken();
        var codeVerifier = CreateRandomToken();
        _pending[state] = new PendingSignIn(providerName, codeVerifier, timeProvider.GetUtcNow());

        var url = authenticator.BuildAuthorizationUrl(
            new AuthorizationRequest(redirectUri, state, CreateCodeChallenge(codeVerifier)));

        return Task.FromResult(url);
    }

    public async Task<CalendarAccount> CompleteConnectAsync(
        string state,
        string code,
        Uri redirectUri,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (!_pending.TryRemove(state, out var pending))
        {
            throw new InvalidOperationException("This sign-in has expired or was already used. Start it again.");
        }

        var authenticator = registry.GetAuthenticator(pending.ProviderName);
        var account = await authenticator.CompleteAuthorizationAsync(
            new AuthorizationCallback(redirectUri, code, pending.CodeVerifier),
            cancellationToken);

        var existing = (await accountStore.GetAllAsync(cancellationToken)).FirstOrDefault(candidate =>
            string.Equals(candidate.ProviderName, account.ProviderName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.DisplayName, account.DisplayName, StringComparison.OrdinalIgnoreCase));

        // Reconnecting a calendar keeps its identity so existing mirror links stay valid.
        var connected = existing is null ? account : account with { Id = existing.Id, Enabled = existing.Enabled };
        await accountStore.SaveAsync(connected, cancellationToken);

        LogConnected(connected.ProviderName, connected.DisplayName);
        return connected;
    }

    public async Task SetEnabledAsync(Guid accountId, bool enabled, CancellationToken cancellationToken)
    {
        var account = await accountStore.GetAsync(accountId, cancellationToken)
                      ?? throw new InvalidOperationException($"No connected account with id {accountId}.");

        await accountStore.SaveAsync(account with { Enabled = enabled }, cancellationToken);
    }

    public async Task DisconnectAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await accountStore.GetAsync(accountId, cancellationToken);
        if (account is null)
        {
            return;
        }

        var links = await stateStore.GetLinksForAccountAsync(accountId, cancellationToken);
        foreach (var link in links)
        {
            await TryDeleteMirrorAsync(link, cancellationToken);
        }

        await stateStore.RemoveLinksForAccountAsync(accountId, cancellationToken);
        await accountStore.RemoveAsync(accountId, cancellationToken);

        LogDisconnected(account.ProviderName, account.DisplayName);
    }

    private async Task TryDeleteMirrorAsync(SyncLink link, CancellationToken cancellationToken)
    {
        try
        {
            var targetAccount = await accountStore.GetAsync(link.TargetAccountId, cancellationToken);
            if (targetAccount is null)
            {
                return;
            }

            var authenticator = registry.GetAuthenticator(targetAccount.ProviderName);
            var authorised = await authenticator.EnsureAccessTokenAsync(targetAccount, cancellationToken);
            await registry.GetProvider(authorised.ProviderName)
                .DeleteEventAsync(authorised, link.TargetEventId, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogMirrorRemovalFailed(exception, link.TargetEventId);
        }
    }

    private void PrunePendingSignIns()
    {
        var cutoff = timeProvider.GetUtcNow() - PendingSignInLifetime;
        foreach (var (state, pending) in _pending)
        {
            if (pending.StartedAt < cutoff)
            {
                _pending.TryRemove(state, out _);
            }
        }
    }

    private static string CreateRandomToken() => Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string CreateCodeChallenge(string codeVerifier) =>
        Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [LoggerMessage(Level = LogLevel.Information, Message = "Connected {Provider} account {Account}")]
    private partial void LogConnected(string provider, string account);

    [LoggerMessage(Level = LogLevel.Information, Message = "Disconnected {Provider} account {Account}")]
    private partial void LogDisconnected(string provider, string account);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove mirrored event {EventId} while disconnecting")]
    private partial void LogMirrorRemovalFailed(Exception exception, string eventId);

    private sealed record PendingSignIn(string ProviderName, string CodeVerifier, DateTimeOffset StartedAt);
}
