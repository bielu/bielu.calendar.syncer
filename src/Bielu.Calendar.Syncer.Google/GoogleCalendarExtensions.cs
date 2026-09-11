using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bielu.Calendar.Syncer.Google;

public static class GoogleCalendarExtensions
{
    /// <summary>Adds Google Calendar as a syncable provider.</summary>
    public static ICalendarSyncerBuilder AddGoogleCalendar(
        this ICalendarSyncerBuilder builder,
        Action<GoogleCalendarOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var optionsBuilder = builder.Services.AddOptions<GoogleCalendarOptions>();
        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
        }

        builder.Services.AddHttpClient(GoogleCalendarProvider.Name);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ICalendarProvider, GoogleCalendarProvider>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<ICalendarAuthenticator, GoogleCalendarAuthenticator>());

        return builder;
    }
}
