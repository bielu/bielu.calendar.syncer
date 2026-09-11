using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bielu.Calendar.Syncer;

public static class CalendarSyncerExtensions
{
    /// <summary>Registers the mirroring engine, local state stores and the background worker.</summary>
    public static ICalendarSyncerBuilder AddCalendarSyncer(
        this IServiceCollection services,
        Action<CalendarSyncerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var optionsBuilder = services.AddOptions<CalendarSyncerOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAccountStore, JsonAccountStore>();
        services.TryAddSingleton<ISyncStateStore, JsonSyncStateStore>();
        services.TryAddSingleton<ISyncStatusTracker, SyncStatusTracker>();
        services.TryAddSingleton<ICalendarProviderRegistry, CalendarProviderRegistry>();
        services.TryAddSingleton<IAccountManager, AccountManager>();

        services.TryAddSingleton<CalendarSyncer>();
        services.TryAddSingleton<ICalendarSyncer>(provider => new StatusTrackingCalendarSyncer(
            provider.GetRequiredService<CalendarSyncer>(),
            provider.GetRequiredService<ISyncStatusTracker>()));

        services.AddHostedService<CalendarSyncWorker>();

        return new CalendarSyncerBuilder(services);
    }
}

/// <summary>Fluent surface that provider slices hang their own registration extensions off.</summary>
public interface ICalendarSyncerBuilder
{
    IServiceCollection Services { get; }
}

internal sealed class CalendarSyncerBuilder(IServiceCollection services) : ICalendarSyncerBuilder
{
    public IServiceCollection Services { get; } = services;
}
