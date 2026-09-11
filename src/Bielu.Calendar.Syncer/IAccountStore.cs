namespace Bielu.Calendar.Syncer;

/// <summary>Persists the set of connected calendar accounts, including their refresh tokens.</summary>
public interface IAccountStore
{
    Task<IReadOnlyList<CalendarAccount>> GetAllAsync(CancellationToken cancellationToken);

    Task<CalendarAccount?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task SaveAsync(CalendarAccount account, CancellationToken cancellationToken);

    Task RemoveAsync(Guid id, CancellationToken cancellationToken);
}
