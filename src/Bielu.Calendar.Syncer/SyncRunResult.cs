namespace Bielu.Calendar.Syncer;

public sealed record SyncRunResult
{
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public int Created { get; init; }
    public int Updated { get; init; }
    public int Deleted { get; init; }
    public int AccountsSynced { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public bool Succeeded => Errors.Count == 0;
    public TimeSpan Duration => CompletedAt - StartedAt;
}
