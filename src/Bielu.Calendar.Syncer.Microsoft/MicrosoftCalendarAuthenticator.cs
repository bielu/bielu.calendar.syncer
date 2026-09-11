using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Bielu.Calendar.Syncer.Microsoft;

internal sealed class MicrosoftCalendarAuthenticator(
    IHttpClientFactory httpClientFactory,
    TimeProvider timeProvider,
    IOptions<MicrosoftCalendarOptions> options) : OAuth2Authenticator(httpClientFactory, timeProvider)
{
    private readonly MicrosoftCalendarOptions _options = options.Value;

    public override string ProviderName => MicrosoftCalendarProvider.Name;

    public override bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ClientId);

    protected override Uri AuthorizationEndpoint =>
        new($"https://login.microsoftonline.com/{_options.Tenant}/oauth2/v2.0/authorize");

    protected override Uri TokenEndpoint =>
        new($"https://login.microsoftonline.com/{_options.Tenant}/oauth2/v2.0/token");

    protected override string ClientId => _options.ClientId;

    // offline_access is what makes Entra ID return a refresh token.
    protected override string Scope => "offline_access openid email User.Read Calendars.ReadWrite";

    protected override async Task<string> ResolveDisplayNameAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var client = CreateHttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var me = await response.Content.ReadFromJsonAsync<GraphUser>(cancellationToken);
        return me?.Mail ?? me?.UserPrincipalName ?? "Outlook calendar";
    }

    private sealed record GraphUser(string? Mail, string? UserPrincipalName);
}
