namespace Bielu.Calendar.Syncer;

/// <summary>Drives the browser OAuth handshake that connects a new account, and keeps its tokens fresh.</summary>
public interface ICalendarAuthenticator
{
    /// <summary>Matches <see cref="ICalendarProvider.ProviderName"/> of the provider this authenticator serves.</summary>
    string ProviderName { get; }

    /// <summary>True when the provider has been given the client credentials it needs to start a sign-in.</summary>
    bool IsConfigured { get; }

    /// <summary>Builds the consent URL the dashboard sends the browser to.</summary>
    Uri BuildAuthorizationUrl(AuthorizationRequest request);

    /// <summary>Exchanges the authorization code returned to the dashboard for a connected account.</summary>
    Task<CalendarAccount> CompleteAuthorizationAsync(
        AuthorizationCallback callback,
        CancellationToken cancellationToken);

    /// <summary>Returns an account whose access token is valid, refreshing it when it has expired.</summary>
    Task<CalendarAccount> EnsureAccessTokenAsync(CalendarAccount account, CancellationToken cancellationToken);
}

/// <param name="RedirectUri">Dashboard callback endpoint the provider redirects back to.</param>
/// <param name="State">Opaque value echoed back by the provider, used to match the pending sign-in.</param>
/// <param name="CodeChallenge">PKCE S256 challenge derived from the pending sign-in's verifier.</param>
public sealed record AuthorizationRequest(Uri RedirectUri, string State, string CodeChallenge);

/// <param name="Code">Authorization code handed back by the provider.</param>
/// <param name="CodeVerifier">PKCE verifier matching the challenge sent in <see cref="AuthorizationRequest"/>.</param>
public sealed record AuthorizationCallback(Uri RedirectUri, string Code, string CodeVerifier);
