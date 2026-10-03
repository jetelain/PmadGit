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

    /// <summary>
    /// Collects commits reachable from <paramref name="fromTip"/> that are not reachable from <paramref name="target"/>
    /// and not present on any remote tracking ref.
    /// </summary>
    public static async Task<IReadOnlyList<GitCommitSummary>> CollectLostCommitsAsync(
        IGitRepository repo,
        GitHash fromTip,
        GitHash target,
        CancellationToken ct)
    {
        var result = new List<GitCommitSummary>();
        await foreach (var commit in repo.EnumerateCommitsAsync(fromTip.ToString(), ct).ConfigureAwait(false))
        {
            if (await repo.IsCommitReachableAsync(from: target, to: commit.Id, ct).ConfigureAwait(false))
            {
                continue;
            }
            if (!await repo.IsCommitPushedAsync(commit.Id, null, ct).ConfigureAwait(false))
            {
                result.Add(ToSummary(commit));
                if (result.Count >= 50)
                {
                    break;
                }
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

    /// <summary>
    /// Finds untracked files in the working directory that collide with paths in <paramref name="targetCommit"/>.
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetUntrackedCollisionsAsync(
        IGitWorkspaceRepository repo,
        GitCommit targetCommit,
        CancellationToken ct)
    {
        var collisions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var index = await GitIndex.ReadAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes, ct).ConfigureAwait(false);
        var trackedPaths = index.Entries
            .Select(e => e.Path.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await foreach (var item in repo.EnumerateCommitTreeAsync(targetCommit.Id.ToString(), null, SearchOption.AllDirectories, ct).ConfigureAwait(false))
        {
            if (item.Entry.Kind == GitTreeEntryKind.Blob)
            {
                var normalizedPath = item.Path.Replace('\\', '/');
                var fullPath = Path.Combine(repo.RootPath, normalizedPath.Replace('/', Path.DirectorySeparatorChar));

                // 1. Direct collision: target is a blob and disk has a file that is not tracked
                if (File.Exists(fullPath) && !trackedPaths.Contains(normalizedPath))
                {
                    collisions.Add(normalizedPath);
                }

                // 2. Directory-to-file collision: target is 'build' and disk has directory 'build'
                if (Directory.Exists(fullPath) && !File.Exists(fullPath))
                {
                    try
                    {
                        var filesInDir = Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories);
                        foreach (var diskFile in filesInDir)
                        {
                            var relPath = Path.GetRelativePath(repo.RootPath, diskFile).Replace('\\', '/');
                            if (!trackedPaths.Contains(relPath))
                            {
                                collisions.Add(relPath);
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                // 3. File-to-directory collision: target is 'foo/bar' and an ancestor 'foo' on disk is a file that is not tracked
                var parts = normalizedPath.Split('/');
                var prefix = "";
                for (var i = 0; i < parts.Length - 1; i++)
                {
                    prefix = i == 0 ? parts[0] : prefix + "/" + parts[i];
                    var ancestorFullPath = Path.Combine(repo.RootPath, prefix.Replace('/', Path.DirectorySeparatorChar));
                    if (File.Exists(ancestorFullPath) && !trackedPaths.Contains(prefix))
                    {
                        collisions.Add(prefix);
                    }
                }
            }
        }

        // Also check status.UntrackedEntries as fallback
        var status = await repo.GetStatusAsync(includeUntracked: true, cancellationToken: ct).ConfigureAwait(false);
        var untrackedEntries = status.UntrackedEntries
            .Select(e => e.Path.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (untrackedEntries.Count > 0)
        {
            await foreach (var item in repo.EnumerateCommitTreeAsync(targetCommit.Id.ToString(), null, SearchOption.AllDirectories, ct).ConfigureAwait(false))
            {
                if (item.Entry.Kind == GitTreeEntryKind.Blob)
                {
                    var normalizedPath = item.Path.Replace('\\', '/');
                    if (untrackedEntries.Contains(normalizedPath))
                    {
                        collisions.Add(normalizedPath);
                    }
                    else
                    {
                        foreach (var untracked in untrackedEntries)
                        {
                            if (normalizedPath.StartsWith(untracked + "/", StringComparison.OrdinalIgnoreCase)
                                || untracked.StartsWith(normalizedPath + "/", StringComparison.OrdinalIgnoreCase))
                            {
                                collisions.Add(untracked);
                            }
                        }
                    }
                }
            }
        }

        return collisions.ToList();
    }
}
