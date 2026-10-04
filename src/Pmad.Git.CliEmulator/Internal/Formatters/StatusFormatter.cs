using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Internal.Formatters;

internal static class StatusFormatter
{
    public static async Task WriteAsync(
        IGitWorkspaceRepository repo,
        TextWriter writer,
        CancellationToken ct)
    {
        var branch = await repo.GetCurrentBranchNameAsync(ct).ConfigureAwait(false);
        var isDetached = await repo.IsHeadDetachedAsync(ct).ConfigureAwait(false);

        if (isDetached)
        {
            var head = await repo.GetCommitAsync(cancellationToken: ct).ConfigureAwait(false);
            await writer.WriteLineAsync($"HEAD detached at {head.Id.ToString()[..7]}");
        }
        else
        {
            await writer.WriteLineAsync($"On branch {branch}");

            // Tracking status
            try
            {
                var tracking = await repo.GetTrackingStatusAsync(branch, ct).ConfigureAwait(false);
                if (tracking.HasUpstream)
                {
                    if (tracking.IsSynchronized)
                    {
                        await writer.WriteLineAsync($"Your branch is up to date with '{tracking.UpstreamBranch}'.");
                    }
                    else if (tracking.AheadCount > 0 && tracking.BehindCount == 0)
                    {
                        await writer.WriteLineAsync($"Your branch is ahead of '{tracking.UpstreamBranch}' by {tracking.AheadCount} commit{(tracking.AheadCount == 1 ? "" : "s")}.");
                    }
                    else if (tracking.BehindCount > 0 && tracking.AheadCount == 0)
                    {
                        await writer.WriteLineAsync($"Your branch is behind '{tracking.UpstreamBranch}' by {tracking.BehindCount} commit{(tracking.BehindCount == 1 ? "" : "s")}, and can be fast-forwarded.");
                    }
                    else if (tracking.AheadCount > 0 && tracking.BehindCount > 0)
                    {
                        await writer.WriteLineAsync($"Your branch and '{tracking.UpstreamBranch}' have diverged,");
                        await writer.WriteLineAsync($"and have {tracking.AheadCount} and {tracking.BehindCount} different commits each, respectively.");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { /* tracking info is best-effort */ }
        }

        await writer.WriteLineAsync().ConfigureAwait(false);

        var status = await repo.GetStatusAsync(includeUntracked: true, cancellationToken: ct).ConfigureAwait(false);

        // Check merge in progress
        var mergeInProgress = await repo.IsMergeInProgressAsync(ct).ConfigureAwait(false);
        if (mergeInProgress)
        {
            await writer.WriteLineAsync("You have unmerged paths.").ConfigureAwait(false);
            await writer.WriteLineAsync("  (fix conflicts and run \"git commit\")").ConfigureAwait(false);
            await writer.WriteLineAsync("  (use \"git merge --abort\" to abort the merge)").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
        }

        if (status.ConflictedEntries.Count > 0)
        {
            await writer.WriteLineAsync("Unmerged paths:").ConfigureAwait(false);
            await writer.WriteLineAsync("  (use \"git add <file>...\" to mark resolution)").ConfigureAwait(false);
            foreach (var e in status.ConflictedEntries)
            {
                await writer.WriteLineAsync($"\t       both modified:   {e.Path}").ConfigureAwait(false);
            }
            await writer.WriteLineAsync().ConfigureAwait(false);
        }

        if (status.StagedEntries.Count > 0)
        {
            await writer.WriteLineAsync("Changes to be committed:").ConfigureAwait(false);
            await writer.WriteLineAsync("  (use \"git restore --staged <file>...\" to unstage)").ConfigureAwait(false);
            foreach (var e in status.StagedEntries)
            {
                var label = e.StagedStatus switch
                {
                    GitFileStatus.StagedNew => "new file:  ",
                    GitFileStatus.StagedModified => "modified:  ",
                    GitFileStatus.StagedDeleted => "deleted:   ",
                    _ => "           "
                };
                await writer.WriteLineAsync($"\t{label}   {e.Path}").ConfigureAwait(false);
            }
            await writer.WriteLineAsync().ConfigureAwait(false);
        }

        var unstaged = status.ModifiedEntries.Concat(status.DeletedEntries).OrderBy(e => e.Path).ToList();
        if (unstaged.Count > 0)
        {
            await writer.WriteLineAsync("Changes not staged for commit:").ConfigureAwait(false);
            await writer.WriteLineAsync("  (use \"git add <file>...\" to update what will be committed)").ConfigureAwait(false);
            await writer.WriteLineAsync("  (use \"git restore <file>...\" to discard changes in working directory)").ConfigureAwait(false);
            foreach (var e in unstaged)
            {
                var label = e.WorkingTreeStatus == GitFileStatus.Deleted ? "deleted:   " : "modified:  ";
                await writer.WriteLineAsync($"\t{label}   {e.Path}").ConfigureAwait(false);
            }
            await writer.WriteLineAsync().ConfigureAwait(false);
        }

        if (status.UntrackedEntries.Count > 0)
        {
            await writer.WriteLineAsync("Untracked files:").ConfigureAwait(false);
            await writer.WriteLineAsync("  (use \"git add <file>...\" to include in what will be committed)").ConfigureAwait(false);
            foreach (var e in status.UntrackedEntries)
            {
                await writer.WriteLineAsync($"\t{e.Path}").ConfigureAwait(false);
            }
            await writer.WriteLineAsync().ConfigureAwait(false);
        }

        if (status.IsClean && !mergeInProgress)
        {
            await writer.WriteLineAsync("nothing to commit, working tree clean").ConfigureAwait(false);
        }
    }

    public static async Task WriteShortAsync(
        IGitWorkspaceRepository repo,
        TextWriter writer,
        CancellationToken ct)
    {
        var status = await repo.GetStatusAsync(includeUntracked: true, cancellationToken: ct).ConfigureAwait(false);
        var entries = status.Entries.Where(e => !e.IsClean).ToList();

        // Staged renames detection: pair StagedDeleted and StagedNew with matching blob hashes
        var renames = new Dictionary<string, (GitStatusEntry Deleted, GitStatusEntry Added)>(StringComparer.Ordinal);
        var matchedDeleted = new HashSet<string>(StringComparer.Ordinal);
        var matchedAdded = new HashSet<string>(StringComparer.Ordinal);

        var stagedDeleted = entries.Where(e => e.StagedStatus == GitFileStatus.StagedDeleted && e.HeadHash.HasValue).ToList();
        var stagedNew = entries.Where(e => e.StagedStatus == GitFileStatus.StagedNew && e.IndexHash.HasValue).ToList();

        foreach (var added in stagedNew)
        {
            var match = stagedDeleted.FirstOrDefault(d => !matchedDeleted.Contains(d.Path) && d.HeadHash!.Value.Equals(added.IndexHash!.Value));
            if (match != null)
            {
                renames[added.Path] = (match, added);
                matchedDeleted.Add(match.Path);
                matchedAdded.Add(added.Path);
            }
        }

        foreach (var e in entries.OrderBy(e => e.Path, StringComparer.Ordinal))
        {
            if (matchedDeleted.Contains(e.Path))
            {
                continue;
            }

            if (renames.TryGetValue(e.Path, out var renamePair))
            {
                var worktreeCode = renamePair.Added.WorkingTreeStatus switch
                {
                    GitFileStatus.Modified => 'M',
                    GitFileStatus.Deleted => 'D',
                    _ => ' '
                };
                await writer.WriteLineAsync($"R{worktreeCode} {renamePair.Deleted.Path} -> {renamePair.Added.Path}").ConfigureAwait(false);
                continue;
            }

            if (e.IsConflicted)
            {
                await writer.WriteLineAsync($"UU {e.Path}").ConfigureAwait(false);
                continue;
            }
            char stagedCode = e.StagedStatus switch
            {
                GitFileStatus.StagedNew => 'A',
                GitFileStatus.StagedModified => 'M',
                GitFileStatus.StagedDeleted => 'D',
                _ => ' '
            };
            char worktreeCodeChar = e.WorkingTreeStatus switch
            {
                GitFileStatus.Modified => 'M',
                GitFileStatus.Deleted => 'D',
                GitFileStatus.Untracked => '?',
                _ => ' '
            };
            if (worktreeCodeChar == '?')
            {
                stagedCode = '?';
            }
            await writer.WriteLineAsync($"{stagedCode}{worktreeCodeChar} {e.Path}").ConfigureAwait(false);
        }
    }
}
