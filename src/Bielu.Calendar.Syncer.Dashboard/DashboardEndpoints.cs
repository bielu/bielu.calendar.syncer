using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Bielu.Calendar.Syncer.Dashboard;

public static class DashboardEndpoints
{
    private const string IndexResourceName = "Bielu.Calendar.Syncer.Dashboard.Assets.index.html";

    /// <summary>Maps the dashboard page, its status API and the OAuth callback used to connect accounts.</summary>
    public static IEndpointRouteBuilder MapCalendarSyncerDashboard(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet("/", () => Results.Extensions.EmbeddedHtml(IndexResourceName));

        endpoints.MapGet(
            "/api/status",
            async (IAccountManager accounts, ISyncStatusTracker tracker, ICalendarProviderRegistry registry, CancellationToken cancellationToken) =>
            {
                var connected = await accounts.ListAsync(cancellationToken);
                return Results.Ok(DashboardStatus.From(connected, tracker, registry));
            });

        endpoints.MapPost(
            "/api/sync",
            async (ICalendarSyncer syncer, CancellationToken cancellationToken) =>
                Results.Ok(await syncer.SyncAsync(cancellationToken)));

        endpoints.MapGet(
            "/api/connect/{provider}",
            async (string provider, HttpContext context, IAccountManager accounts, CancellationToken cancellationToken) =>
            {
                var url = await accounts.BeginConnectAsync(provider, BuildRedirectUri(context), cancellationToken);
                return Results.Redirect(url.ToString());
            });

        endpoints.MapGet(
            "/api/connect/callback",
            async (
                HttpContext context,
                IAccountManager accounts,
                CancellationToken cancellationToken,
                string? code = null,
                string? state = null,
                string? error = null) =>
            {
                if (!string.IsNullOrEmpty(error))
                {
                    return Results.Redirect($"/?error={Uri.EscapeDataString(error)}");
                }

                if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
                {
                    return Results.Redirect("/?error=missing_authorization_code");
                }

                await accounts.CompleteConnectAsync(state, code, BuildRedirectUri(context), cancellationToken);
                return Results.Redirect("/?connected=1");
            });

        endpoints.MapPost(
            "/api/accounts/{id:guid}/enabled",
            async (Guid id, EnabledRequest request, IAccountManager accounts, CancellationToken cancellationToken) =>
            {
                await accounts.SetEnabledAsync(id, request.Enabled, cancellationToken);
                return Results.NoContent();
            });

        endpoints.MapDelete(
            "/api/accounts/{id:guid}",
            async (Guid id, IAccountManager accounts, CancellationToken cancellationToken) =>
            {
                await accounts.DisconnectAsync(id, cancellationToken);
                return Results.NoContent();
            });

        return endpoints;
    }

    /// <summary>
    /// The redirect URI must be byte-identical between the authorize call and the token exchange, so it is derived
    /// the same way in both places rather than configured twice.
    /// </summary>
    private static Uri BuildRedirectUri(HttpContext context) =>
        new($"{context.Request.Scheme}://{context.Request.Host}/api/connect/callback");

    private sealed record EnabledRequest(bool Enabled);
}

internal static class ResultsExtensions
{
    public static IResult EmbeddedHtml(this IResultExtensions _, string resourceName)
    {
        var assembly = typeof(DashboardEndpoints).Assembly;
        var stream = assembly.GetManifestResourceStream(resourceName)
                     ?? throw new InvalidOperationException($"Dashboard asset '{resourceName}' is missing from the assembly.");

        return Results.Stream(stream, "text/html; charset=utf-8");
    }
}
