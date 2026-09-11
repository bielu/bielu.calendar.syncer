using Microsoft.Extensions.Options;

namespace Bielu.Calendar.Syncer;

internal sealed class JsonAccountStore : IDisposable, IAccountStore
{
    private readonly JsonFileStore<List<CalendarAccount>> _store;

    public JsonAccountStore(IOptions<CalendarSyncerOptions> options) =>
        _store = new JsonFileStore<List<CalendarAccount>>(
            Path.Combine(options.Value.DataDirectory, "accounts.json"),
            () => []);

    public void Dispose() => _store.Dispose();

    public async Task<IReadOnlyList<CalendarAccount>> GetAllAsync(CancellationToken cancellationToken) =>
        await _store.ReadAsync(accounts => (IReadOnlyList<CalendarAccount>)accounts.ToList(), cancellationToken);

    public Task<CalendarAccount?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        _store.ReadAsync(accounts => accounts.FirstOrDefault(account => account.Id == id), cancellationToken);

    public Task SaveAsync(CalendarAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        return _store.MutateAsync(
            accounts =>
            {
                accounts.RemoveAll(existing => existing.Id == account.Id);
                accounts.Add(account);
            },
            cancellationToken);
    }

    public Task RemoveAsync(Guid id, CancellationToken cancellationToken) =>
        _store.MutateAsync(accounts => accounts.RemoveAll(account => account.Id == id), cancellationToken);
}
