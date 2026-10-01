using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Internal;

/// <summary>Static helpers that build approval context objects and call the appropriate approval method.</summary>
internal static class ApprovalHelper
{
    /// <summary>
    /// Awaits the given approval method and throws the appropriate exception if not approved.
    /// <list type="bullet">
    ///   <item><see cref="ApprovalResult.Denied"/> → throws <see cref="GitCliDeniedException"/>.</item>
    ///   <item><see cref="ApprovalResult.Cancelled"/> → throws <see cref="OperationCanceledException"/>.</item>
    /// </list>
    /// </summary>
    public static async Task RequireAsync<T>(T context, Func<T, CancellationToken, Task<ApprovalResult>> approvalMethod, CancellationToken ct) where T : class, IApprovalContext
    {
        var result = await approvalMethod(context, ct).ConfigureAwait(false);
        if (result == ApprovalResult.Approved)
        {
            return;
        }
        if (result == ApprovalResult.Cancelled)
        {
            throw new OperationCanceledException($"Approval for '{context.Operation}' was cancelled.");
        }
        throw new GitCliDeniedException(context.Operation);
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
        var result = new List<GitCommitSummary>();
        await foreach (var commit in repo.EnumerateCommitsAsync(branchName, ct).ConfigureAwait(false))
        {
            if (await repo.IsCommitPushedAsync(commit.Id, null, ct).ConfigureAwait(false))
            {
                break;
            }
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
