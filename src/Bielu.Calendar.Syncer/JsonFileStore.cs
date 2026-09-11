using System.Text.Json;

namespace Bielu.Calendar.Syncer;

/// <summary>
/// Reads and writes one JSON document under the data directory, serialising access so concurrent callers cannot
/// interleave a read-modify-write. Files hold OAuth refresh tokens, so they are created owner-only.
/// </summary>
internal sealed class JsonFileStore<T>(string filePath, Func<T> createEmpty) : IDisposable
    where T : class
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private T? _cache;

    public void Dispose() => _gate.Dispose();

    public async Task<TResult> ReadAsync<TResult>(Func<T, TResult> read, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return read(await LoadAsync(cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MutateAsync(Action<T> mutate, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await LoadAsync(cancellationToken);
            mutate(state);
            await PersistAsync(state, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<T> LoadAsync(CancellationToken cancellationToken)
    {
        if (_cache is not null)
        {
            return _cache;
        }

        if (!File.Exists(filePath))
        {
            return _cache = createEmpty();
        }

        await using var stream = File.OpenRead(filePath);
        _cache = await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken)
                 ?? createEmpty();
        return _cache;
    }

    private async Task PersistAsync(T state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        var temporaryPath = filePath + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken);
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temporaryPath, filePath, overwrite: true);
        _cache = state;
    }
}
