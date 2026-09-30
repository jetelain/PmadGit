using System.CommandLine;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Commands;

internal static class MergeCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("merge") { Description = "Join two or more development histories together" };
        var noFfOpt = new Option<bool>("--no-ff") { Description = "Create a merge commit even for fast-forwards" };
        var ffOnlyOpt = new Option<bool>("--ff-only") { Description = "Refuse to merge unless fast-forward is possible" };
        var messageOpt = new Option<string?>("-m") { Description = "Merge commit message" };
        var abortOpt = new Option<bool>("--abort") { Description = "Abort an in-progress merge" };
        var continueOpt = new Option<bool>("--continue") { Description = "Continue after resolving conflicts" };
        var branchArg = new Argument<string?>("branch") { Description = "Branch or commit to merge", Arity = ArgumentArity.ZeroOrOne };

        cmd.Options.Add(noFfOpt);
        cmd.Options.Add(ffOnlyOpt);
        cmd.Options.Add(messageOpt);
        cmd.Options.Add(abortOpt);
        cmd.Options.Add(continueOpt);
        cmd.Arguments.Add(branchArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var noFf = pr.GetValue(noFfOpt);
            var ffOnly = pr.GetValue(ffOnlyOpt);
            var message = pr.GetValue(messageOpt);
            var abort = pr.GetValue(abortOpt);
            var cont = pr.GetValue(continueOpt);
            var branch = pr.GetValue(branchArg);

            try
            {
                if (abort)
                {
                    var conflicts = await ctx.Repository.GetConflictedFilesAsync(ct);
                    await ApprovalHelper.RequireAsync(
                        ctx.Approval.ApproveDiscardLocalChangesAsync(new DiscardChangesContext
                        {
                            Operation = "merge --abort",
                            AffectedFiles = conflicts,
                        }, ct),
                        "merge --abort");
                    await ctx.Repository.AbortMergeAsync(ct);
                    await ctx.StdOut.WriteLineAsync("Merge aborted.");
                    return 0;
                }

                if (cont)
                {
                    var hash = await ctx.Repository.ContinueMergeAsync(message, cancellationToken: ct);
                    var currentBranch = await ctx.Repository.GetCurrentBranchNameAsync(ct) ?? "HEAD";
                    await ctx.StdOut.WriteLineAsync($"[{currentBranch} {hash.ToString()[..7]}] Merge commit");
                    return 0;
                }

                if (string.IsNullOrEmpty(branch))
                {
                    return ctx.WriteError("Branch name required.");
                }

                var options = new GitMergeOptions
                {
                    NoFastForward = noFf,
                    FastForwardOnly = ffOnly,
                    CommitMessage = message,
                };

                var result = await ctx.Repository.MergeAsync(branch, options, ct);
                if (result.IsSuccess)
                {
                    var status = result.Status switch
                    {
                        GitMergeStatus.AlreadyUpToDate => "Already up to date.",
                        GitMergeStatus.FastForward => $"Fast-forward\n{(result.CommitHash.HasValue ? $"Merge made by fast-forward. Commit: {result.CommitHash.Value.ToString()[..7]}" : "")}",
                        _ => $"Merge made by the 'ort' strategy.{(result.CommitHash.HasValue ? $" Commit: {result.CommitHash.Value.ToString()[..7]}" : "")}"
                    };
                    await ctx.StdOut.WriteLineAsync(status);
                    return 0;
                }
                else
                {
                    await ctx.StdOut.WriteLineAsync("Automatic merge failed; fix conflicts and then commit the result.");
                    foreach (var f in result.ConflictedFiles)
                    {
                        await ctx.StdErr.WriteLineAsync($"CONFLICT: {f}");
                    }
                    return 1;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not GitCliDeniedException)
            {
                return ctx.WriteError(ex.Message);
            }
        });
        return cmd;
    }
}
