using System.CommandLine;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Commands;

internal static class SwitchCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("switch") { Description = "Switch branches" };

        var createOpt = new Option<string?>("-c", "--create") { Description = "Create and switch to a new branch" };
        var forceCreateOpt = new Option<string?>("-C", "--force-create") { Description = "Create/reset and switch to a branch" };
        var detachOpt = new Option<bool>("-d", "--detach") { Description = "Detach HEAD at named commit" };
        var discardChangesOpt = new Option<bool>("--discard-changes") { Description = "Proceed even if the index or the working tree differs from HEAD" };
        var forceOpt = new Option<bool>("-f", "--force") { Description = "Proceed even if the index or the working tree differs from HEAD" };
        var orphanOpt = new Option<string?>("--orphan") { Description = "Create a new orphan branch, starting with an empty index and working tree" };

        var branchArg = new Argument<string?>("branch") { Description = "Branch name or commit to switch/detach to", Arity = ArgumentArity.ZeroOrOne };
        var startPointArg = new Argument<string?>("start-point") { Description = "Start point when creating a new branch", Arity = ArgumentArity.ZeroOrOne };

        cmd.Options.Add(createOpt);
        cmd.Options.Add(forceCreateOpt);
        cmd.Options.Add(detachOpt);
        cmd.Options.Add(discardChangesOpt);
        cmd.Options.Add(forceOpt);
        cmd.Options.Add(orphanOpt);

        cmd.Arguments.Add(branchArg);
        cmd.Arguments.Add(startPointArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var isDetach = pr.GetValue(detachOpt);
            var forceBranchName = pr.GetValue(forceCreateOpt);
            var regularCreateBranchName = pr.GetValue(createOpt);
            var isForceCreate = !string.IsNullOrEmpty(forceBranchName);
            var isCreate = isForceCreate || !string.IsNullOrEmpty(regularCreateBranchName);
            var createBranchName = forceBranchName ?? regularCreateBranchName;
            var isDiscardChanges = pr.GetValue(discardChangesOpt) || pr.GetValue(forceOpt);
            var orphanBranch = pr.GetValue(orphanOpt);

            // Switching using --force / --discard-changes now fails if there are unmerged entries
            if (await ctx.Repository.IsMergeInProgressAsync(ct).ConfigureAwait(false))
            {
                return ctx.WriteError("Cannot switch branch while a merge is in progress.");
            }
            var conflicted = await ctx.Repository.GetConflictedFilesAsync(ct).ConfigureAwait(false);
            if (conflicted.Count > 0)
            {
                return ctx.WriteError("Cannot switch branch with unmerged (conflicted) entries in the index.");
            }

            // Case 1: Orphan branch
            if (!string.IsNullOrEmpty(orphanBranch))
            {
                var startPt = pr.GetValue(branchArg) ?? pr.GetValue(startPointArg);
                if (!string.IsNullOrWhiteSpace(startPt))
                {
                    return ctx.WriteError("Cannot use start-point with --orphan.");
                }

                try
                {
                    var status = await ctx.Repository.GetStatusAsync(includeUntracked: false, cancellationToken: ct).ConfigureAwait(false);
                    var changedFiles = status.Entries
                        .Where(e => !e.IsClean)
                        .Select(e => e.Path)
                        .ToList();

                    if (changedFiles.Count > 0)
                    {
                        await ApprovalHelper.RequireAsync(
                            new DiscardChangesContext
                            {
                                Operation = "switch --orphan",
                                AffectedFiles = changedFiles,
                            },
                            ctx.Approval.ApproveDiscardLocalChangesAsync,
                            ct).ConfigureAwait(false);
                    }

                    await ctx.Repository.CheckoutOrphanBranchAsync(orphanBranch, empty: true, startPoint: null, cancellationToken: ct).ConfigureAwait(false);
                    await ctx.StdOut.WriteLineAsync($"Switched to a new branch '{orphanBranch}'").ConfigureAwait(false);
                    return 0;
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not GitCliDeniedException)
                {
                    return ctx.WriteError(ex.Message);
                }
            }

            string? branchOrCommit;
            string? startPoint;

            if (isCreate)
            {
                branchOrCommit = createBranchName;
                startPoint = pr.GetValue(branchArg);
            }
            else
            {
                branchOrCommit = pr.GetValue(branchArg);
                startPoint = pr.GetValue(startPointArg);
            }

            if (string.IsNullOrWhiteSpace(branchOrCommit))
            {
                return ctx.WriteError(isDetach ? "Missing commit to detach to." : "Missing branch name.");
            }

            try
            {
                // Gate 1: Discard local uncommitted changes
                if (isDiscardChanges)
                {
                    var status = await ctx.Repository.GetStatusAsync(includeUntracked: false, cancellationToken: ct).ConfigureAwait(false);
                    var changedFiles = status.Entries
                        .Where(e => !e.IsClean)
                        .Select(e => e.Path)
                        .ToList();

                    if (changedFiles.Count > 0)
                    {
                        var opName = pr.GetValue(discardChangesOpt) ? "switch --discard-changes" : "switch -f";
                        await ApprovalHelper.RequireAsync(
                            new DiscardChangesContext
                            {
                                Operation = opName,
                                AffectedFiles = changedFiles,
                            },
                            ctx.Approval.ApproveDiscardLocalChangesAsync,
                            ct).ConfigureAwait(false);
                    }
                }

                if (isDetach)
                {
                    var targetCommit = await ctx.Repository.GetCommitAsync(branchOrCommit, ct).ConfigureAwait(false);

                    // Gate 2a: Unpushed commit loss if currently on a detached HEAD
                    if (await ctx.Repository.IsHeadDetachedAsync(ct).ConfigureAwait(false))
                    {
                        var currentHead = await ctx.Repository.ReferenceStore.ResolveHeadAsync(ct).ConfigureAwait(false);
                        if (!currentHead.Equals(targetCommit.Id))
                        {
                            var lost = await ApprovalHelper.CollectLostCommitsAsync(ctx.Repository, currentHead, targetCommit.Id, ct).ConfigureAwait(false);
                            if (lost.Count > 0)
                            {
                                await ApprovalHelper.RequireAsync(
                                    new UnpushedCommitLossContext
                                    {
                                        Operation = "switch --detach",
                                        BranchName = "HEAD",
                                        CommitsToLose = lost,
                                    },
                                    ctx.Approval.ApproveUnpushedCommitLossAsync,
                                    ct).ConfigureAwait(false);
                            }
                        }
                    }

                    await ctx.Repository.CheckoutCommitAsync(targetCommit.Id.ToString(), force: isDiscardChanges, cancellationToken: ct).ConfigureAwait(false);
                    await ctx.StdOut.WriteLineAsync($"HEAD is now at {targetCommit.Id.ToString()[..7]} {targetCommit.Message.Split('\n', 2)[0].Trim()}").ConfigureAwait(false);
                    return 0;
                }

                var targetBranch = branchOrCommit;
                if (!isDetach)
                {
                    var normalized = branchOrCommit.Trim().Replace('\\', '/');
                    if (normalized.StartsWith("refs/heads/", StringComparison.Ordinal))
                    {
                        normalized = normalized["refs/heads/".Length..];
                    }
                    else if (normalized.StartsWith("heads/", StringComparison.Ordinal))
                    {
                        normalized = normalized["heads/".Length..];
                    }
                    targetBranch = normalized;
                }

                // If not creating and not detaching:
                // --detach (or -d) is now always required when switching to a detached head.
                // A local branch is expected.
                var branchRef = await ctx.Repository.ReferenceStore.TryResolveReferenceAsync($"refs/heads/{targetBranch}", ct).ConfigureAwait(false);
                if (!isCreate && !branchRef.HasValue)
                {
                    try
                    {
                        await ctx.Repository.GetCommitAsync(branchOrCommit, ct).ConfigureAwait(false);
                        return ctx.WriteError($"fatal: a branch is expected, got commit '{branchOrCommit}'\nIf you want to detach HEAD at the commit, try switch --detach {branchOrCommit}");
                    }
                    catch
                    {
                        return ctx.WriteError($"fatal: invalid reference: {branchOrCommit}");
                    }
                }

                // Branch checkout
                var currentBranch = await ctx.Repository.GetCurrentBranchNameAsync(ct).ConfigureAwait(false);
                var isCurrentBranch = !isCreate && !isDiscardChanges && string.Equals(currentBranch, targetBranch, StringComparison.Ordinal);
                if (isCurrentBranch)
                {
                    await ctx.StdOut.WriteLineAsync($"Already on '{targetBranch}'").ConfigureAwait(false);
                    return 0;
                }

                // Resolve target commit for loss check
                GitCommit? targetCommitForLoss = null;
                if (isCreate)
                {
                    if (startPoint == null)
                    {
                        var headHash = await ctx.Repository.ReferenceStore.TryResolveReferenceAsync("HEAD", ct).ConfigureAwait(false);
                        if (headHash.HasValue)
                        {
                            targetCommitForLoss = await ctx.Repository.GetCommitAsync(headHash.Value.ToString(), ct).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        targetCommitForLoss = await ctx.Repository.GetCommitAsync(startPoint, ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    targetCommitForLoss = await ctx.Repository.GetCommitAsync(branchRef!.Value.ToString(), ct).ConfigureAwait(false);
                }

                // Gate 2b: Unpushed commit loss if force-resetting an existing branch (-C)
                if (isForceCreate && branchRef.HasValue && targetCommitForLoss != null)
                {
                    if (!branchRef.Value.Equals(targetCommitForLoss.Id))
                    {
                        var lost = await ApprovalHelper.CollectLostCommitsAsync(ctx.Repository, branchRef.Value, targetCommitForLoss.Id, ct).ConfigureAwait(false);
                        if (lost.Count > 0)
                        {
                            await ApprovalHelper.RequireAsync(
                                new UnpushedCommitLossContext
                                {
                                    Operation = "switch -C",
                                    BranchName = branchOrCommit,
                                    CommitsToLose = lost,
                                },
                                ctx.Approval.ApproveUnpushedCommitLossAsync,
                                ct).ConfigureAwait(false);
                        }
                    }
                }

                // Gate 2c: Unpushed commit loss if switching away from detached HEAD
                if (await ctx.Repository.IsHeadDetachedAsync(ct).ConfigureAwait(false) && targetCommitForLoss != null)
                {
                    var currentHead = await ctx.Repository.ReferenceStore.ResolveHeadAsync(ct).ConfigureAwait(false);
                    if (!currentHead.Equals(targetCommitForLoss.Id))
                    {
                        var lost = await ApprovalHelper.CollectLostCommitsAsync(ctx.Repository, currentHead, targetCommitForLoss.Id, ct).ConfigureAwait(false);
                        if (lost.Count > 0)
                        {
                            await ApprovalHelper.RequireAsync(
                                new UnpushedCommitLossContext
                                {
                                    Operation = "switch",
                                    BranchName = "HEAD",
                                    CommitsToLose = lost,
                                },
                                ctx.Approval.ApproveUnpushedCommitLossAsync,
                                ct).ConfigureAwait(false);
                        }
                    }
                }

                await ctx.Repository.CheckoutBranchAsync(
                    targetBranch,
                    createBranch: isCreate,
                    startPoint: startPoint,
                    force: isForceCreate || isDiscardChanges,
                    cancellationToken: ct).ConfigureAwait(false);

                if (isCreate)
                {
                    if (isForceCreate && branchRef.HasValue)
                    {
                        await ctx.StdOut.WriteLineAsync($"Reset branch '{targetBranch}'").ConfigureAwait(false);
                    }
                    else
                    {
                        await ctx.StdOut.WriteLineAsync($"Switched to a new branch '{targetBranch}'").ConfigureAwait(false);
                    }
                }
                else
                {
                    await ctx.StdOut.WriteLineAsync($"Switched to branch '{targetBranch}'").ConfigureAwait(false);
                }
                return 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not GitCliDeniedException)
            {
                return ctx.WriteError(ex.Message);
            }
        });

        return cmd;
    }
}
