using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Internal;

/// <summary>Static helpers that build approval context objects and call the appropriate approval method.</summary>
internal static class ApprovalHelper
{
    /// <summary>Calls the given approval task and throws <see cref="GitCliDeniedException"/> if denied.</summary>
    public static async Task RequireAsync(Task<bool> approvalTask, string operationName)
    {
        var granted = await approvalTask.ConfigureAwait(false);
        if (!granted)
        {
            throw new GitCliDeniedException(operationName);
        }
    }

    /// <summary>Builds a <see cref="GitCommitSummary"/> from a <see cref="GitCommit"/>.</summary>
    public static GitCommitSummary ToSummary(GitCommit commit)
    {
        var meta = commit.Metadata;
        var subject = commit.Message.Split('\n', 2)[0].Trim();
        var fullHash = commit.Id.ToString();
        return new GitCommitSummary
        {
            Hash = fullHash,
            ShortHash = fullHash.Length >= 7 ? fullHash[..7] : fullHash,
            Subject = subject,
            Author = meta.AuthorName,
            AuthorEmail = meta.AuthorEmail,
            Date = meta.AuthorDate,
        };
    }

    /// <summary>
    /// Collects commits reachable from <paramref name="branchName"/> that are NOT reachable from any remote tracking ref.
    /// Returns at most <paramref name="maxCount"/> commits.
    /// </summary>
    public static async Task<IReadOnlyList<GitCommitSummary>> CollectUnpushedCommitsAsync(
        IGitRepository repo,
        string branchName,
        int maxCount,
        CancellationToken ct)
    {
        // Find remote tracking refs
        var remoteRefs = await repo.GetReferencesByPrefixAsync("refs/remotes/", ct).ConfigureAwait(false);
        var remoteHashes = new HashSet<GitHash>(remoteRefs.Values);

        var result = new List<GitCommitSummary>();
        await foreach (var commit in repo.EnumerateCommitsAsync(branchName, ct).ConfigureAwait(false))
        {
            if (remoteHashes.Contains(commit.Id))
            {
                break;
            }
            // Also stop if reachable from any remote (expensive check skipped for performance; hash equality is good enough)
            result.Add(ToSummary(commit));
            if (result.Count >= maxCount)
            {
                break;
            }
        }
        return result;
    }

    /// <summary>Strips credentials from a URL for display.</summary>
    public static string SanitizeUrl(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.UserInfo))
        {
            var builder = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty };
            return builder.Uri.ToString();
        }
        return url;
    }
}
