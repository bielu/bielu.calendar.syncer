namespace Bielu.Calendar.Syncer;

internal sealed class CalendarProviderRegistry : ICalendarProviderRegistry
{
    private readonly Dictionary<string, ICalendarProvider> _providers;
    private readonly Dictionary<string, ICalendarAuthenticator> _authenticators;

    public CalendarProviderRegistry(
        IEnumerable<ICalendarProvider> providers,
        IEnumerable<ICalendarAuthenticator> authenticators)
    {
        _providers = providers.ToDictionary(provider => provider.ProviderName, StringComparer.OrdinalIgnoreCase);
        _authenticators = authenticators.ToDictionary(auth => auth.ProviderName, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> ProviderNames => _providers.Keys;

    public ICalendarProvider GetProvider(string providerName) =>
        _providers.TryGetValue(providerName, out var provider)
            ? provider
            : throw new NotSupportedException($"No calendar provider is registered for '{providerName}'.");

    public ICalendarAuthenticator GetAuthenticator(string providerName) =>
        _authenticators.TryGetValue(providerName, out var authenticator)
            ? authenticator
            : throw new NotSupportedException($"No calendar authenticator is registered for '{providerName}'.");

    public bool TryGetAuthenticator(string providerName, out ICalendarAuthenticator authenticator) =>
        _authenticators.TryGetValue(providerName, out authenticator!);
}
