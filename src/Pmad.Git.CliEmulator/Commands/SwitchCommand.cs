using System.CommandLine;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Commands;

internal static class SwitchCommand
{
    public static Command Build(CommandContext ctx)
    {
        return BuildCore("switch", "Switch branches", ctx);
    }

    public static Command BuildCheckout(CommandContext ctx)
    {
        return BuildCore("checkout", "Switch branches or restore working tree files", ctx);
    }

    private static Command BuildCore(string commandName, string description, CommandContext ctx)
    {
        var cmd = new Command(commandName) { Description = description };

        var createOpt = new Option<string?>("-c", "--create") { Description = "Create and switch to a new branch" };
        var forceCreateOpt = new Option<string?>("-C", "--force-create") { Description = "Create/reset and switch to a branch" };
        var bOpt = new Option<string?>("-b") { Description = "Create and checkout a new branch" };
        var forceBOpt = new Option<string?>("-B") { Description = "Create/reset and checkout a branch" };
        var detachOpt = new Option<bool>("-d", "--detach") { Description = "Detach HEAD at named commit" };
        var discardChangesOpt = new Option<bool>("--discard-changes") { Description = "Proceed even if the index or the working tree differs from HEAD" };
        var forceOpt = new Option<bool>("-f", "--force") { Description = "Proceed even if the index or the working tree differs from HEAD" };

        var branchArg = new Argument<string?>("branch") { Description = "Branch name or commit to switch/detach to", Arity = ArgumentArity.ZeroOrOne };
        var startPointArg = new Argument<string?>("start-point") { Description = "Start point when creating a new branch", Arity = ArgumentArity.ZeroOrOne };

        cmd.Options.Add(createOpt);
        cmd.Options.Add(forceCreateOpt);
        cmd.Options.Add(bOpt);
        cmd.Options.Add(forceBOpt);
        cmd.Options.Add(detachOpt);
        cmd.Options.Add(discardChangesOpt);
        cmd.Options.Add(forceOpt);

        cmd.Arguments.Add(branchArg);
        cmd.Arguments.Add(startPointArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var isDetach = pr.GetValue(detachOpt);
            var forceBranchName = pr.GetValue(forceCreateOpt) ?? pr.GetValue(forceBOpt);
            var regularCreateBranchName = pr.GetValue(createOpt) ?? pr.GetValue(bOpt);
            var isForceCreate = !string.IsNullOrEmpty(forceBranchName);
            var isCreate = isForceCreate || !string.IsNullOrEmpty(regularCreateBranchName);
            var createBranchName = forceBranchName ?? regularCreateBranchName;
            var isDiscardChanges = pr.GetValue(discardChangesOpt) || pr.GetValue(forceOpt);

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
                        var opName = commandName == "switch"
                            ? (pr.GetValue(discardChangesOpt) ? "switch --discard-changes" : "switch -f")
                            : "checkout -f";

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
                                        Operation = commandName == "switch" ? "switch --detach" : "checkout --detach",
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

                // Branch checkout
                var currentBranch = await ctx.Repository.GetCurrentBranchNameAsync(ct).ConfigureAwait(false);
                var isCurrentBranch = !isCreate && !isDiscardChanges && string.Equals(currentBranch, branchOrCommit, StringComparison.Ordinal);
                if (isCurrentBranch)
                {
                    await ctx.StdOut.WriteLineAsync($"Already on '{branchOrCommit}'").ConfigureAwait(false);
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
                    var existingHash = await ctx.Repository.ReferenceStore.TryResolveReferenceAsync($"refs/heads/{branchOrCommit}", ct).ConfigureAwait(false);
                    if (existingHash.HasValue)
                    {
                        targetCommitForLoss = await ctx.Repository.GetCommitAsync(existingHash.Value.ToString(), ct).ConfigureAwait(false);
                    }
                }

                // Gate 2b: Unpushed commit loss if force-resetting an existing branch
                var existingBranchRef = await ctx.Repository.ReferenceStore.TryResolveReferenceAsync($"refs/heads/{branchOrCommit}", ct).ConfigureAwait(false);
                if (isForceCreate && existingBranchRef.HasValue && targetCommitForLoss != null)
                {
                    if (!existingBranchRef.Value.Equals(targetCommitForLoss.Id))
                    {
                        var lost = await ApprovalHelper.CollectLostCommitsAsync(ctx.Repository, existingBranchRef.Value, targetCommitForLoss.Id, ct).ConfigureAwait(false);
                        if (lost.Count > 0)
                        {
                            await ApprovalHelper.RequireAsync(
                                new UnpushedCommitLossContext
                                {
                                    Operation = commandName == "switch" ? "switch -C" : "checkout -B",
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
                                    Operation = commandName == "switch" ? "switch" : "checkout",
                                    BranchName = "HEAD",
                                    CommitsToLose = lost,
                                },
                                ctx.Approval.ApproveUnpushedCommitLossAsync,
                                ct).ConfigureAwait(false);
                        }
                    }
                }

                await ctx.Repository.CheckoutBranchAsync(
                    branchOrCommit,
                    createBranch: isCreate,
                    startPoint: startPoint,
                    force: isForceCreate || isDiscardChanges,
                    cancellationToken: ct).ConfigureAwait(false);

                if (isCreate)
                {
                    if (isForceCreate && existingBranchRef.HasValue)
                    {
                        await ctx.StdOut.WriteLineAsync($"Reset branch '{branchOrCommit}'").ConfigureAwait(false);
                    }
                    else
                    {
                        await ctx.StdOut.WriteLineAsync($"Switched to a new branch '{branchOrCommit}'").ConfigureAwait(false);
                    }
                }
                else
                {
                    await ctx.StdOut.WriteLineAsync($"Switched to branch '{branchOrCommit}'").ConfigureAwait(false);
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
