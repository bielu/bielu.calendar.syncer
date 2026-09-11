namespace Bielu.Calendar.Syncer.Dashboard;

/// <summary>Everything the dashboard page renders, in one payload so a poll is a single request.</summary>
public sealed record DashboardStatus
{
    public required bool IsRunning { get; init; }
    public required DateTimeOffset? NextRunAt { get; init; }
    public required SyncRunResult? LastRun { get; init; }
    public required IReadOnlyList<SyncRunResult> History { get; init; }
    public required IReadOnlyList<DashboardAccount> Accounts { get; init; }
    public required IReadOnlyList<DashboardProvider> Providers { get; init; }

    internal static DashboardStatus From(
        IReadOnlyList<CalendarAccount> accounts,
        ISyncStatusTracker tracker,
        ICalendarProviderRegistry registry) =>
        new()
        {
            IsRunning = tracker.IsRunning,
            NextRunAt = tracker.NextRunAt,
            LastRun = tracker.LastRun,
            History = tracker.History,
            Accounts = [.. accounts
                .OrderBy(account => account.ProviderName, StringComparer.Ordinal)
                .ThenBy(account => account.DisplayName, StringComparer.Ordinal)
                .Select(DashboardAccount.From)],
            Providers = [.. registry.ProviderNames
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => DashboardProvider.From(name, registry))],
        };
}

public sealed record DashboardAccount
{
    public required Guid Id { get; init; }
    public required string ProviderName { get; init; }
    public required string DisplayName { get; init; }
    public required bool Enabled { get; init; }
    public required DateTimeOffset ConnectedAt { get; init; }

    internal static DashboardAccount From(CalendarAccount account) =>
        new()
        {
            Id = account.Id,
            ProviderName = account.ProviderName,
            DisplayName = account.DisplayName,
            Enabled = account.Enabled,
            ConnectedAt = account.ConnectedAt,
        };
}

public sealed record DashboardProvider
{
    public required string Name { get; init; }

    /// <summary>False when the provider has no client id yet, which the page turns into a setup hint.</summary>
    public required bool IsConfigured { get; init; }

    internal static DashboardProvider From(string name, ICalendarProviderRegistry registry) =>
        new()
        {
            Name = name,
            IsConfigured = registry.TryGetAuthenticator(name, out var authenticator) && authenticator.IsConfigured,
        };
}
