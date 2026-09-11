namespace Bielu.Calendar.Syncer;

/// <summary>Resolves the provider and authenticator slices registered for a given provider name.</summary>
public interface ICalendarProviderRegistry
{
    IReadOnlyCollection<string> ProviderNames { get; }

    ICalendarProvider GetProvider(string providerName);

    ICalendarAuthenticator GetAuthenticator(string providerName);

    bool TryGetAuthenticator(string providerName, out ICalendarAuthenticator authenticator);
}
