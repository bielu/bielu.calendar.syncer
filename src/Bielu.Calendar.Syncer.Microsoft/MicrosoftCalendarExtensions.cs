using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bielu.Calendar.Syncer.Microsoft;

public static class MicrosoftCalendarExtensions
{
    /// <summary>Adds Outlook / Microsoft 365 calendars as a syncable provider.</summary>
    public static ICalendarSyncerBuilder AddMicrosoftCalendar(
        this ICalendarSyncerBuilder builder,
        Action<MicrosoftCalendarOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = builder.Services.AddOptions<MicrosoftCalendarOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        builder.Services.AddHttpClient(MicrosoftCalendarProvider.Name);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ICalendarProvider, MicrosoftCalendarProvider>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ICalendarAuthenticator, MicrosoftCalendarAuthenticator>());

        return builder;
    }
}
