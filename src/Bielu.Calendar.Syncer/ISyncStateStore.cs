namespace Bielu.Calendar.Syncer;

/// <summary>Remembers which mirrored event on a target account corresponds to which event on a source account.</summary>
public interface ISyncStateStore
{
    Task<IReadOnlyList<SyncLink>> GetLinksAsync(Guid sourceAccountId, Guid targetAccountId, CancellationToken cancellationToken);

    /// <summary>Every link where the account is either the source or the target of a mirror.</summary>
    Task<IReadOnlyList<SyncLink>> GetLinksForAccountAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Adds or replaces links. Batched because each call rewrites the whole state file.</summary>
    Task SaveLinksAsync(IReadOnlyCollection<SyncLink> links, CancellationToken cancellationToken);

    Task RemoveLinksAsync(IReadOnlyCollection<SyncLink> links, CancellationToken cancellationToken);

    /// <summary>Drops every link referencing an account, used when an account is disconnected.</summary>
    Task RemoveLinksForAccountAsync(Guid accountId, CancellationToken cancellationToken);
}

public sealed record SyncLink
{
    public required Guid SourceAccountId { get; init; }
    public required string SourceEventId { get; init; }
    public required Guid TargetAccountId { get; init; }
    public required string TargetEventId { get; init; }

    /// <summary>Last-modified stamp of the source event when the mirror was last written.</summary>
    public DateTimeOffset? SourceLastModified { get; init; }

    /// <summary>Start of the source event, used to tell a deleted event apart from one that left the sync window.</summary>
    public required DateTimeOffset SourceStart { get; init; }
}
