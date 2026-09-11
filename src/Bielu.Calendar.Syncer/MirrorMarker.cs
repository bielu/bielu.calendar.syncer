using System.Text.RegularExpressions;

namespace Bielu.Calendar.Syncer;

/// <summary>
/// Identifies the origin of a mirrored event. Embedded in the mirrored event's body so that mirrors are
/// recognisable even if the local sync state is lost, which is what stops events bouncing between accounts.
/// </summary>
public sealed partial record MirrorMarker(Guid SourceAccountId, string SourceEventId)
{
    private const string Prefix = "[bielu-sync:";

    public string ToBodyTag() => $"{Prefix}{SourceAccountId:N}:{SourceEventId}]";

    public static MirrorMarker? TryParse(string? body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return null;
        }

        var match = MarkerRegex().Match(body);
        return match.Success && Guid.TryParseExact(match.Groups[1].Value, "N", out var accountId)
            ? new MirrorMarker(accountId, match.Groups[2].Value)
            : null;
    }

    [GeneratedRegex(@"\[bielu-sync:([0-9a-fA-F]{32}):([^\]]+)\]", RegexOptions.Compiled)]
    private static partial Regex MarkerRegex();
}
