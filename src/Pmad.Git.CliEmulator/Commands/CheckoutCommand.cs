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
            if (!isCreate && string.IsNullOrEmpty(orphanBranch) && !isDetach && (hasDoubleDash || ShouldTreatAsFileRestore(rawArgs, ctx.Repository, out _, out _)))
            {
                return await HandleFileRestoreAsync(ctx, rawArgs, hasDoubleDash, pr, ct).ConfigureAwait(false);
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

    private static bool ShouldTreatAsFileRestore(string[] args, IGitWorkspaceRepository repo, out string? source, out List<string> paths)
    {
        source = null;
        paths = [];

        if (args.Length == 0)
        {
            return false;
        }

        if (args.Length > 1)
        {
            // Multiple arguments without branch options -> treat as file restore:
            // either [<tree-ish>] <path1> <path2>... or <path1> <path2>...
            // Check if first arg could be a commit/branch and second is a file in worktree/repo
            var first = args[0];
            var secondFullPath = Path.Combine(repo.RootPath, args[1].Replace('/', Path.DirectorySeparatorChar));
            if ((File.Exists(secondFullPath) || Directory.Exists(secondFullPath)) && repo.ReferenceStore.TryResolveReferenceAsync($"refs/heads/{first}").GetAwaiter().GetResult() != null)
            {
                source = first;
                paths.AddRange(args.Skip(1));
                return true;
            }

            paths.AddRange(args);
            return true;
        }

        // Single argument: check if it matches an existing file in the worktree or index
        var singleArg = args[0];
        // If it's a known branch, it's not a file restore unless disambiguated with --
        var branchRef = repo.ReferenceStore.TryResolveReferenceAsync($"refs/heads/{singleArg}").GetAwaiter().GetResult();
        if (branchRef.HasValue)
        {
            return false;
        }

        var fullPath = Path.Combine(repo.RootPath, singleArg.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            paths.Add(singleArg);
            return true;
        }

        return false;
    }

    private static async Task<int> HandleFileRestoreAsync(
        CommandContext ctx,
        string[] args,
        bool hasDoubleDash,
        ParseResult pr,
        CancellationToken ct)
    {
        string? source = null;
        var paths = new List<string>();

        if (hasDoubleDash)
        {
            var doubleDashIndex = -1;
            for (var i = 0; i < pr.Tokens.Count; i++)
            {
                if (pr.Tokens[i].Value == "--")
                {
                    doubleDashIndex = i;
                    break;
                }
            }

            // Tokens before '--' that are not option tokens
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
        else
        {
            ShouldTreatAsFileRestore(args, ctx.Repository, out source, out paths);
        }

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

            await ctx.Repository.CheckoutOrphanBranchAsync(orphanBranch, empty: false, startPoint: startPoint, cancellationToken: ct).ConfigureAwait(false);
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
            // Gate 1: Discard local uncommitted changes
            if (isForce)
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
                            Operation = "checkout -f",
                            AffectedFiles = changedFiles,
                        },
                        ctx.Approval.ApproveDiscardLocalChangesAsync,
                        ct).ConfigureAwait(false);
                }
            }

            var branchRef = await ctx.Repository.ReferenceStore.TryResolveReferenceAsync($"refs/heads/{branchOrCommit}", ct).ConfigureAwait(false);
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
                    catch
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
            var isCurrentBranch = !isCreate && !isForce && string.Equals(currentBranch, branchOrCommit, StringComparison.Ordinal);
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
                targetCommitForLoss = await ctx.Repository.GetCommitAsync(branchRef!.Value.ToString(), ct).ConfigureAwait(false);
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
                branchOrCommit,
                createBranch: isCreate,
                startPoint: startPoint,
                force: isForceCreate || isForce,
                cancellationToken: ct).ConfigureAwait(false);

            if (isCreate)
            {
                if (isForceCreate && branchRef.HasValue)
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
    }
}
