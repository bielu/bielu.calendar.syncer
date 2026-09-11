using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Bielu.Calendar.Syncer.Google;

internal sealed class GoogleCalendarAuthenticator(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    IOptions<GoogleCalendarOptions> options) : OAuth2Authenticator(httpClientFactory, timeProvider)
{
    private readonly GoogleCalendarOptions _options = options.Value;

    public override string ProviderName => GoogleCalendarProvider.Name;

    public override bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ClientId);

    protected override Uri AuthorizationEndpoint { get; } = new("https://accounts.google.com/o/oauth2/v2/auth");

    protected override Uri TokenEndpoint { get; } = new("https://oauth2.googleapis.com/token");

    protected override string ClientId => _options.ClientId;

    protected override string? ClientSecret => _options.ClientSecret;

    protected override string Scope => "openid email https://www.googleapis.com/auth/calendar";

    protected override IEnumerable<KeyValuePair<string, string>> ExtraAuthorizationParameters =>
    [
        // Offline access is what yields a refresh token, and forcing the consent screen makes Google re-issue it
        // when the same calendar is reconnected.
        new("access_type", "offline"),
        new("prompt", "consent"),
    ];

    protected override async Task<string> ResolveDisplayNameAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var client = CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v3/userinfo");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var info = await response.Content.ReadFromJsonAsync<UserInfo>(cancellationToken);
        return string.IsNullOrWhiteSpace(info?.Email) ? "Google calendar" : info.Email;
    }

    private sealed record UserInfo([property: JsonPropertyName("email")] string? Email);
}
