using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Internal.Formatters;

internal static class BranchFormatter
{
    public static async Task WriteAsync(
        IGitRepository repo,
        bool includeRemote,
        bool verbose,
        TextWriter writer,
        CancellationToken ct)
    {
        var currentBranch = await repo.GetCurrentBranchNameAsync(ct).ConfigureAwait(false);
        var branches = await repo.GetBranchesAsync(includeRemote: false, ct).ConfigureAwait(false);

        foreach (var branch in branches.OrderBy(b => b, StringComparer.Ordinal))
        {
            var isCurrent = string.Equals(branch, currentBranch, StringComparison.Ordinal);
            var prefix = isCurrent ? "* " : "  ";

            if (verbose)
            {
                try
                {
                    var commit = await repo.GetCommitAsync($"refs/heads/{branch}", ct).ConfigureAwait(false);
                    var shortHash = commit.Id.ToString()[..7];
                    var subject = commit.Message.Split('\n', 2)[0].Trim();
                    await writer.WriteLineAsync($"{prefix}{branch,-30} {shortHash} {subject}").ConfigureAwait(false);
                }
                catch
                {
                    await writer.WriteLineAsync($"{prefix}{branch}").ConfigureAwait(false);
                }
            }
            else
            {
                await writer.WriteLineAsync($"{prefix}{branch}").ConfigureAwait(false);
            }
        }

        if (includeRemote)
        {
            var remoteBranches = await repo.GetBranchesAsync(includeRemote: true, ct).ConfigureAwait(false);
            var remoteOnly = remoteBranches.Except(branches).OrderBy(b => b, StringComparer.Ordinal);
            foreach (var branch in remoteOnly)
            {
                await writer.WriteLineAsync($"  remotes/{branch}").ConfigureAwait(false);
            }
        }
    }
}
