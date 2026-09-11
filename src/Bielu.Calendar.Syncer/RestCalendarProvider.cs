using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bielu.Calendar.Syncer;

/// <summary>Shared HTTP plumbing for providers that talk to a JSON REST calendar API.</summary>
public abstract class RestCalendarProvider(IHttpClientFactory httpClientFactory) : ICalendarProvider
{
    /// <summary>
    /// Nulls are dropped so a write payload carries only the fields being set. Both APIs reject requests that
    /// include server-owned fields such as the event id or its last-modified stamp.
    /// </summary>
    protected static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public abstract string ProviderName { get; }

    public abstract Task<IReadOnlyList<CalendarEvent>> GetEventsAsync(
        CalendarAccount account,
        SyncWindow window,
        CancellationToken cancellationToken);

    public abstract Task<string> CreateEventAsync(
        CalendarAccount account,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken);

    public abstract Task UpdateEventAsync(
        CalendarAccount account,
        string eventId,
        CalendarEvent calendarEvent,
        CancellationToken cancellationToken);

    public abstract Task DeleteEventAsync(
        CalendarAccount account,
        string eventId,
        CancellationToken cancellationToken);

    protected HttpClient CreateHttpClient() => httpClientFactory.CreateClient(ProviderName);

    protected async Task<TResponse> SendAsync<TResponse>(
        CalendarAccount account,
        HttpMethod method,
        Uri uri,
        object? payload,
        CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(account, method, uri, payload, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<TResponse>(SerializerOptions, cancellationToken)
               ?? throw new InvalidOperationException($"{ProviderName} returned an empty response for {uri}.");
    }

    /// <summary>Sends a request that has no response body, treating a missing resource as already gone.</summary>
    protected async Task SendIgnoringMissingAsync(
        CalendarAccount account,
        HttpMethod method,
        Uri uri,
        object? payload,
        CancellationToken cancellationToken)
    {
        using var response = await SendCoreAsync(account, method, uri, payload, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            return;
        }

        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(
        CalendarAccount account,
        HttpMethod method,
        Uri uri,
        object? payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        using var client = CreateHttpClient();
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);

        if (payload is not null)
        {
            request.Content = JsonContent.Create(payload, options: SerializerOptions);
        }

        ConfigureRequest(request);
        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>Hook for provider-specific headers, such as the time zone a response should be rendered in.</summary>
    protected virtual void ConfigureRequest(HttpRequestMessage request)
    {
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{ProviderName} request to {response.RequestMessage?.RequestUri} failed with {(int)response.StatusCode}: {body}"));
    }
}
