using System.CommandLine;
using System.CommandLine.Parsing;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Commands;

internal static class CheckoutCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("checkout") { Description = "Switch branches or restore working tree files" };

        var bOpt = new Option<string?>("-b") { Description = "Create and checkout a new branch" };
        var forceBOpt = new Option<string?>("-B") { Description = "Create/reset and checkout a branch" };
        var detachOpt = new Option<bool>("--detach") { Description = "Detach HEAD at named commit" };
        var forceOpt = new Option<bool>("-f", "--force") { Description = "Proceed even if the index or the working tree differs from HEAD" };
        var orphanOpt = new Option<string?>("--orphan") { Description = "Create a new orphan branch" };

        var argsArg = new Argument<string[]>("args") { Description = "Branch, commit, or file paths", Arity = ArgumentArity.ZeroOrMore };

        cmd.Options.Add(bOpt);
        cmd.Options.Add(forceBOpt);
        cmd.Options.Add(detachOpt);
        cmd.Options.Add(forceOpt);
        cmd.Options.Add(orphanOpt);

        cmd.Arguments.Add(argsArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var isForce = pr.GetValue(forceOpt);
            var isDetach = pr.GetValue(detachOpt);
            var forceBranchName = pr.GetValue(forceBOpt);
            var regularCreateBranchName = pr.GetValue(bOpt);
            var isForceCreate = !string.IsNullOrEmpty(forceBranchName);
            var isCreate = isForceCreate || !string.IsNullOrEmpty(regularCreateBranchName);
            var createBranchName = forceBranchName ?? regularCreateBranchName;
            var orphanBranch = pr.GetValue(orphanOpt);
            var rawArgs = pr.GetValue(argsArg) ?? [];

            var hasDoubleDash = pr.Tokens.Any(t => t.Value == "--");

            // Case 1: File restoration mode
            if (!isCreate && string.IsNullOrEmpty(orphanBranch) && !isDetach)
            {
                if (hasDoubleDash)
                {
                    var (source, paths) = ParseDoubleDashFileRestore(pr);
                    return await HandleFileRestoreAsync(ctx, source, paths, ct).ConfigureAwait(false);
                }

                var (isRestore, sourceFromArgs, pathsFromArgs) = await ShouldTreatAsFileRestoreAsync(rawArgs, ctx.Repository, ct).ConfigureAwait(false);
                if (isRestore)
                {
                    return await HandleFileRestoreAsync(ctx, sourceFromArgs, pathsFromArgs, ct).ConfigureAwait(false);
                }
            }

            // Case 2: Orphan branch
            if (!string.IsNullOrEmpty(orphanBranch))
            {
                var startPoint = rawArgs.Length > 0 ? rawArgs[0] : null;
                return await HandleOrphanAsync(ctx, orphanBranch, startPoint, isForce, ct).ConfigureAwait(false);
            }

            // Case 3: Branch / commit checkout
            return await HandleBranchOrCommitCheckoutAsync(
                ctx,
                createBranchName,
                isCreate,
                isForceCreate,
                isDetach,
                isForce,
                rawArgs,
                ct).ConfigureAwait(false);
        });

        return cmd;
    }

    private static (string? source, List<string> paths) ParseDoubleDashFileRestore(ParseResult pr)
    {
        string? source = null;
        var paths = new List<string>();

        var doubleDashIndex = -1;
        for (var i = 0; i < pr.Tokens.Count; i++)
        {
            if (pr.Tokens[i].Value == "--")
            {
                doubleDashIndex = i;
                break;
            }
        }

        if (doubleDashIndex >= 0)
        {
            var nonOptionBeforeDash = pr.Tokens
                .Take(doubleDashIndex)
                .Where(t => t.Type == TokenType.Argument)
                .Select(t => t.Value)
                .ToList();

            if (nonOptionBeforeDash.Count > 0)
            {
                source = nonOptionBeforeDash[0];
            }

            var tokensAfterDash = pr.Tokens
                .Skip(doubleDashIndex + 1)
                .Select(t => t.Value)
                .ToList();

            paths.AddRange(tokensAfterDash);
        }

        return (source, paths);
    }

    private static async Task<(bool isRestore, string? source, List<string> paths)> ShouldTreatAsFileRestoreAsync(
        string[] args,
        IGitWorkspaceRepository repo,
        CancellationToken ct)
    {
        if (args.Length == 0)
        {
            return (false, null, []);
        }

        if (args.Length > 1)
        {
            var first = args[0];
            GitCommit? sourceCommit = null;
            try
            {
                sourceCommit = await repo.GetCommitAsync(first, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
            }

            if (sourceCommit != null)
            {
                return (true, first, args.Skip(1).ToList());
            }

            return (true, null, args.ToList());
        }

        // Single argument: check if it matches a known branch first
        var singleArg = args[0];
        var normalizedBranch = singleArg.Trim().Replace('\\', '/');
        if (normalizedBranch.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            normalizedBranch = normalizedBranch["refs/heads/".Length..];
        }
        else if (normalizedBranch.StartsWith("heads/", StringComparison.Ordinal))
        {
            normalizedBranch = normalizedBranch["heads/".Length..];
        }

        var branchRef = await repo.ReferenceStore.TryResolveReferenceAsync($"refs/heads/{normalizedBranch}", ct).ConfigureAwait(false);
        if (branchRef.HasValue)
        {
            return (false, null, []);
        }

        var normalizedPath = singleArg.Replace('\\', '/').Trim('/');
        var index = await GitIndex.ReadAsync(repo.IndexManager.IndexPath, repo.HashLengthBytes, ct).ConfigureAwait(false);
        if (normalizedPath == "." || normalizedPath.Length == 0 ||
            index.FindEntry(normalizedPath) != null ||
            index.Entries.Any(e => string.Equals(e.Path, normalizedPath, StringComparison.OrdinalIgnoreCase) ||
                                   e.Path.StartsWith(normalizedPath + "/", StringComparison.OrdinalIgnoreCase)))
        {
            return (true, null, [singleArg]);
        }

        return (false, null, []);
    }

    private static async Task<int> HandleFileRestoreAsync(
        CommandContext ctx,
        string? source,
        List<string> paths,
        CancellationToken ct)
    {
        if (paths.Count == 0)
        {
            return ctx.WriteError("No paths specified.");
        }

        try
        {
            var status = await ctx.Repository.GetStatusAsync(includeUntracked: false, cancellationToken: ct).ConfigureAwait(false);
            var dirtyPaths = status.Entries
                .Where(e => !e.IsClean && paths.Any(p => string.Equals(p.Replace('\\', '/'), e.Path.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)))
                .Select(e => e.Path)
                .ToList();

            if (dirtyPaths.Count > 0)
            {
                var opName = source != null ? $"checkout {source} --" : "checkout --";
                await ApprovalHelper.RequireAsync(
                    new DiscardChangesContext
                    {
                        Operation = opName,
                        AffectedFiles = dirtyPaths,
                    },
                    ctx.Approval.ApproveDiscardLocalChangesAsync,
                    ct).ConfigureAwait(false);
            }

            if (source != null)
            {
                await ctx.Repository.RestoreIndexAsync(paths, source, ct).ConfigureAwait(false);
            }

            foreach (var path in paths)
            {
                await ctx.Repository.RestoreFileAsync(path, source, ct).ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not GitCliDeniedException)
        {
            return ctx.WriteError(ex.Message);
        }
    }

    private static async Task<int> HandleOrphanAsync(
        CommandContext ctx,
        string orphanBranch,
        string? startPoint,
        bool isForce,
        CancellationToken ct)
    {
        try
        {
            if (isForce)
            {
                var status = await ctx.Repository.GetStatusAsync(includeUntracked: false, cancellationToken: ct).ConfigureAwait(false);
                var changedFiles = status.Entries
                    .Where(e => !e.IsClean)
                    .Select(e => e.Path)
                    .ToList();

                if (!string.IsNullOrEmpty(startPoint))
                {
                    GitCommit? targetCommit = null;
                    try
                    {
                        targetCommit = await ctx.Repository.GetCommitAsync(startPoint, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                    }

                    if (targetCommit != null)
                    {
                        var collisions = await ApprovalHelper.GetUntrackedCollisionsAsync(ctx.Repository, targetCommit, ct).ConfigureAwait(false);
                        foreach (var c in collisions)
                        {
                            if (!changedFiles.Contains(c, StringComparer.OrdinalIgnoreCase))
                            {
                                changedFiles.Add(c);
                            }
                        }
                    }
                }

                if (changedFiles.Count > 0)
                {
                    await ApprovalHelper.RequireAsync(
                        new DiscardChangesContext
                        {
                            Operation = "checkout -f --orphan",
                            AffectedFiles = changedFiles,
                        },
                        ctx.Approval.ApproveDiscardLocalChangesAsync,
                        ct).ConfigureAwait(false);
                }
            }

            await ctx.Repository.CheckoutOrphanBranchAsync(orphanBranch, empty: false, startPoint: startPoint, force: isForce, cancellationToken: ct).ConfigureAwait(false);
            await ctx.StdOut.WriteLineAsync($"Switched to a new branch '{orphanBranch}'").ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not GitCliDeniedException)
        {
            return ctx.WriteError(ex.Message);
        }
    }

    private static async Task<int> HandleBranchOrCommitCheckoutAsync(
        CommandContext ctx,
        string? createBranchName,
        bool isCreate,
        bool isForceCreate,
        bool isDetach,
        bool isForce,
        string[] args,
        CancellationToken ct)
    {
        string? branchOrCommit;
        string? startPoint = null;

        if (isCreate)
        {
            branchOrCommit = createBranchName;
            startPoint = args.Length > 0 ? args[0] : null;
        }
        else
        {
            branchOrCommit = args.Length > 0 ? args[0] : null;
            startPoint = args.Length > 1 ? args[1] : null;
        }

        if (string.IsNullOrWhiteSpace(branchOrCommit))
        {
            return ctx.WriteError(isDetach ? "Missing commit to detach to." : "Missing branch name or paths to checkout.");
        }

        try
        {
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

            var branchRef = await ctx.Repository.ReferenceStore.TryResolveReferenceAsync($"refs/heads/{targetBranch}", ct).ConfigureAwait(false);
            var isLocalBranch = branchRef.HasValue;

            // In checkout, --detach was optional for commits:
            // if --detach is specified OR (!isCreate && target is not a local branch, but resolves to a commit):
            var shouldDetach = isDetach;
            GitCommit? targetCommit = null;

            if (!isCreate)
            {
                if (shouldDetach || !isLocalBranch)
                {
                    try
                    {
                        targetCommit = await ctx.Repository.GetCommitAsync(branchOrCommit, ct).ConfigureAwait(false);
                        shouldDetach = true;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        if (shouldDetach)
                        {
                            return ctx.WriteError($"fatal: reference is not a tree: {branchOrCommit}");
                        }
                    }
                }
            }

            if (shouldDetach && targetCommit != null)
            {
                // Gate 1: Discard local uncommitted changes + untracked collisions
                if (isForce)
                {
                    var status = await ctx.Repository.GetStatusAsync(includeUntracked: false, cancellationToken: ct).ConfigureAwait(false);
                    var changedFiles = status.Entries
                        .Where(e => !e.IsClean)
                        .Select(e => e.Path)
                        .ToList();

                    var collisions = await ApprovalHelper.GetUntrackedCollisionsAsync(ctx.Repository, targetCommit, ct).ConfigureAwait(false);
                    foreach (var c in collisions)
                    {
                        if (!changedFiles.Contains(c, StringComparer.OrdinalIgnoreCase))
                        {
                            changedFiles.Add(c);
                        }
                    }

                    if (changedFiles.Count > 0)
                    {
                        await ApprovalHelper.RequireAsync(
                            new DiscardChangesContext
                            {
                                Operation = "checkout -f",
                                AffectedFiles = changedFiles,
                            },
                            ctx.Approval.ApproveDiscardLocalChangesAsync,
                            ct).ConfigureAwait(false);
                    }
                }

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
                                    Operation = "checkout --detach",
                                    BranchName = "HEAD",
                                    CommitsToLose = lost,
                                },
                                ctx.Approval.ApproveUnpushedCommitLossAsync,
                                ct).ConfigureAwait(false);
                        }
                    }
                }

                await ctx.Repository.CheckoutCommitAsync(targetCommit.Id.ToString(), force: isForce, cancellationToken: ct).ConfigureAwait(false);
                await ctx.StdOut.WriteLineAsync($"HEAD is now at {targetCommit.Id.ToString()[..7]} {targetCommit.Message.Split('\n', 2)[0].Trim()}").ConfigureAwait(false);
                return 0;
            }

            if (!isCreate && !isLocalBranch)
            {
                return ctx.WriteError($"error: pathspec '{branchOrCommit}' did not match any file(s) known to git");
            }

            // Normal branch checkout
            var currentBranch = await ctx.Repository.GetCurrentBranchNameAsync(ct).ConfigureAwait(false);
            var isCurrentBranch = !isCreate && !isForce && string.Equals(currentBranch, targetBranch, StringComparison.Ordinal);
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

            // Gate 1: Discard local uncommitted changes + untracked collisions
            if (isForce)
            {
                var status = await ctx.Repository.GetStatusAsync(includeUntracked: false, cancellationToken: ct).ConfigureAwait(false);
                var changedFiles = status.Entries
                    .Where(e => !e.IsClean)
                    .Select(e => e.Path)
                    .ToList();

                if (targetCommitForLoss != null)
                {
                    var collisions = await ApprovalHelper.GetUntrackedCollisionsAsync(ctx.Repository, targetCommitForLoss, ct).ConfigureAwait(false);
                    foreach (var c in collisions)
                    {
                        if (!changedFiles.Contains(c, StringComparer.OrdinalIgnoreCase))
                        {
                            changedFiles.Add(c);
                        }
                    }
                }

                if (changedFiles.Count > 0)
                {
                    await ApprovalHelper.RequireAsync(
                        new DiscardChangesContext
                        {
                            Operation = "checkout -f",
                            AffectedFiles = changedFiles,
                        },
                        ctx.Approval.ApproveDiscardLocalChangesAsync,
                        ct).ConfigureAwait(false);
                }
            }

            // Gate 2b: Unpushed commit loss if force-resetting an existing branch (-B)
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
                                Operation = "checkout -B",
                                BranchName = targetBranch,
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
                                Operation = "checkout",
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
                force: isForce,
                overwriteBranch: isForceCreate,
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
    }
}
