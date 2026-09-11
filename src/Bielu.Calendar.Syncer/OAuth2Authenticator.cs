using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Bielu.Calendar.Syncer;

/// <summary>
/// Authorization-code-with-PKCE handshake shared by every provider. Subclasses supply the endpoints, scopes and
/// the call that resolves which mailbox was signed in.
/// </summary>
public abstract class OAuth2Authenticator(IHttpClientFactory httpClientFactory, TimeProvider timeProvider)
    : ICalendarAuthenticator
{
    /// <summary>Access tokens are renewed this long before they actually expire, to absorb clock skew.</summary>
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

    public abstract string ProviderName { get; }

    public abstract bool IsConfigured { get; }

    protected abstract Uri AuthorizationEndpoint { get; }

    protected abstract Uri TokenEndpoint { get; }

    protected abstract string ClientId { get; }

    protected abstract string Scope { get; }

    /// <summary>Only set for providers that issue a non-confidential secret to desktop clients, such as Google.</summary>
    protected virtual string? ClientSecret => null;

    /// <summary>Provider-specific query parameters appended to the consent URL.</summary>
    protected virtual IEnumerable<KeyValuePair<string, string>> ExtraAuthorizationParameters => [];

    protected HttpClient CreateHttpClient() => httpClientFactory.CreateClient(ProviderName);

    /// <summary>Reads the signed-in mailbox address, used as the account's display name.</summary>
    protected abstract Task<string> ResolveDisplayNameAsync(string accessToken, CancellationToken cancellationToken);

    public Uri BuildAuthorizationUrl(AuthorizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = ClientId,
            ["response_type"] = "code",
            ["redirect_uri"] = request.RedirectUri.ToString(),
            ["scope"] = Scope,
            ["state"] = request.State,
            ["code_challenge"] = request.CodeChallenge,
            ["code_challenge_method"] = "S256",
        };

        foreach (var (key, value) in ExtraAuthorizationParameters)
        {
            parameters[key] = value;
        }

        var query = string.Join('&', parameters.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return new Uri($"{AuthorizationEndpoint}?{query}");
    }

    public async Task<CalendarAccount> CompleteAuthorizationAsync(
        AuthorizationCallback callback,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var token = await RequestTokenAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "authorization_code",
                ["code"] = callback.Code,
                ["redirect_uri"] = callback.RedirectUri.ToString(),
                ["code_verifier"] = callback.CodeVerifier,
            },
            cancellationToken);

        if (string.IsNullOrEmpty(token.RefreshToken))
        {
            throw new InvalidOperationException(
                $"{ProviderName} did not return a refresh token, so the account could not be connected for background syncing.");
        }

        var displayName = await ResolveDisplayNameAsync(token.AccessToken, cancellationToken);
        var now = timeProvider.GetUtcNow();

        return new CalendarAccount
        {
            Id = Guid.NewGuid(),
            ProviderName = ProviderName,
            DisplayName = displayName,
            RefreshToken = token.RefreshToken,
            AccessToken = token.AccessToken,
            AccessTokenExpiresAt = now.AddSeconds(token.ExpiresIn),
            ConnectedAt = now,
        };
    }

    public async Task<CalendarAccount> EnsureAccessTokenAsync(CalendarAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (!string.IsNullOrEmpty(account.AccessToken) &&
            account.AccessTokenExpiresAt - RefreshMargin > timeProvider.GetUtcNow())
        {
            return account;
        }

        var token = await RequestTokenAsync(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = account.RefreshToken,
            },
            cancellationToken);

        return account.WithTokens(
            token.AccessToken,
            timeProvider.GetUtcNow().AddSeconds(token.ExpiresIn),
            token.RefreshToken);
    }

    private async Task<TokenResponse> RequestTokenAsync(
        Dictionary<string, string> parameters,
        CancellationToken cancellationToken)
    {
        parameters["client_id"] = ClientId;
        if (!string.IsNullOrEmpty(ClientSecret))
        {
            parameters["client_secret"] = ClientSecret;
        }

        using var client = CreateHttpClient();
        using var content = new FormUrlEncodedContent(parameters);
        using var response = await client.PostAsync(TokenEndpoint, content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{ProviderName} token request failed with {(int)response.StatusCode}: {body}"));
        }

        return await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken)
               ?? throw new InvalidOperationException($"{ProviderName} returned an empty token response.");
    }

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; init; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }
    }
}
