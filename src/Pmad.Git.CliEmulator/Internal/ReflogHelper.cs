using System.IO;

namespace Pmad.Git.CliEmulator.Internal;

internal static class ReflogHelper
{
    public static async Task RecordCheckoutAsync(string gitDirectory, string? oldBranchOrCommit, string newBranchOrCommit, CancellationToken ct = default)
    {
        try
        {
            var logsDir = Path.Combine(gitDirectory, "logs");
            Directory.CreateDirectory(logsDir);
            var headLog = Path.Combine(logsDir, "HEAD");
            var line = $"{DateTimeOffset.UtcNow.ToUnixTimeSeconds()} checkout: moving from {oldBranchOrCommit ?? "HEAD"} to {newBranchOrCommit}\n";
            await File.AppendAllTextAsync(headLog, line, ct).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(oldBranchOrCommit) && oldBranchOrCommit != "-")
            {
                var prevHead = Path.Combine(gitDirectory, "PREV_HEAD");
                await File.WriteAllTextAsync(prevHead, oldBranchOrCommit, ct).ConfigureAwait(false);
            }
        }
        catch
        {
            // Ignore logging errors
        }
    }

    public static async Task<string?> GetPreviousBranchAsync(string gitDirectory, CancellationToken ct = default)
    {
        var logsDir = Path.Combine(gitDirectory, "logs");
        var headLog = Path.Combine(logsDir, "HEAD");
        if (File.Exists(headLog))
        {
            var lines = await File.ReadAllLinesAsync(headLog, ct).ConfigureAwait(false);
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var line = lines[i];
                var idx = line.IndexOf("checkout: moving from ", StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var rem = line[(idx + "checkout: moving from ".Length)..];
                    var toIdx = rem.IndexOf(" to ", StringComparison.Ordinal);
                    if (toIdx > 0)
                    {
                        var prev = rem[..toIdx].Trim();
                        if (!string.IsNullOrEmpty(prev) && prev != "HEAD" && prev != "-")
                        {
                            return prev;
                        }
                    }
                }
            }
        }

        var prevHead = Path.Combine(gitDirectory, "PREV_HEAD");
        if (File.Exists(prevHead))
        {
            var content = (await File.ReadAllTextAsync(prevHead, ct).ConfigureAwait(false)).Trim();
            if (!string.IsNullOrEmpty(content))
            {
                return content;
            }
        }

        return null;
    }
}
