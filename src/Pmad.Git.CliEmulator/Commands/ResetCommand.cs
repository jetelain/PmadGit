using System.CommandLine;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Commands;

internal static class ResetCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("reset") { Description = "Reset current HEAD to the specified state" };
        var softOpt = new Option<bool>("--soft") { Description = "Only move HEAD; keep index and working tree" };
        var mixedOpt = new Option<bool>("--mixed") { Description = "Reset index but not working tree (default)" };
        var hardOpt = new Option<bool>("--hard") { Description = "Reset index and working tree" };
        var commitArg = new Argument<string?>("commit") { Description = "Commit to reset to", Arity = ArgumentArity.ZeroOrOne };

        cmd.Options.Add(softOpt);
        cmd.Options.Add(mixedOpt);
        cmd.Options.Add(hardOpt);
        cmd.Arguments.Add(commitArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var soft = pr.GetValue(softOpt);
            var hard = pr.GetValue(hardOpt);
            var commitRef = pr.GetValue(commitArg) ?? "HEAD";

            var mode = hard ? GitResetMode.Hard : soft ? GitResetMode.Soft : GitResetMode.Mixed;

            try
            {
                var targetCommit = await ctx.Repository.GetCommitAsync(commitRef, ct);
                var branch = await ctx.Repository.GetCurrentBranchNameAsync(ct) ?? "HEAD";
                var headCommit = await ctx.Repository.GetCommitAsync(cancellationToken: ct);

                if (!headCommit.Id.Equals(targetCommit.Id))
                {
                    var lost = await CollectLostCommitsAsync(ctx.Repository, headCommit.Id, targetCommit.Id, ct);
                    if (lost.Count > 0)
                    {
                        await ApprovalHelper.RequireAsync(
                            ctx.Approval.ApproveUnpushedCommitLossAsync(new UnpushedCommitLossContext
                            {
                                Operation = $"reset {(hard ? "--hard" : soft ? "--soft" : "--mixed")}",
                                BranchName = branch,
                                CommitsToLose = lost,
                            }, ct),
                            "reset");
                    }
                }

                if (hard)
                {
                    var status = await ctx.Repository.GetStatusAsync(includeUntracked: false, cancellationToken: ct);
                    var changedFiles = status.Entries
                        .Where(e => !e.IsClean)
                        .Select(e => e.Path)
                        .ToList();

                    if (changedFiles.Count > 0)
                    {
                        await ApprovalHelper.RequireAsync(
                            ctx.Approval.ApproveDiscardLocalChangesAsync(new DiscardChangesContext
                            {
                                Operation = "reset --hard",
                                AffectedFiles = changedFiles,
                            }, ct),
                            "reset --hard");
                    }
                }

                await ctx.Repository.ResetAsync(targetCommit.Id, mode, ct);
                await ctx.StdOut.WriteLineAsync($"HEAD is now at {targetCommit.Id.ToString()[..7]} {targetCommit.Message.Split('\n', 2)[0].Trim()}");
                return 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not GitCliDeniedException)
            {
                return ctx.WriteError(ex.Message);
            }
        });
        return cmd;
    }

    private static async Task<IReadOnlyList<GitCommitSummary>> CollectLostCommitsAsync(
        IGitRepository repo,
        GitHash fromTip,
        GitHash target,
        CancellationToken ct)
    {
        var remoteRefs = await repo.GetReferencesByPrefixAsync("refs/remotes/", ct).ConfigureAwait(false);
        var remoteHashes = new HashSet<GitHash>(remoteRefs.Values);

        var result = new List<GitCommitSummary>();
        await foreach (var commit in repo.EnumerateCommitsAsync(fromTip.ToString(), ct).ConfigureAwait(false))
        {
            if (commit.Id.Equals(target))
            {
                break;
            }
            if (!remoteHashes.Contains(commit.Id))
            {
                result.Add(ApprovalHelper.ToSummary(commit));
            }
            if (result.Count >= 50)
            {
                break;
            }
        }
        return result;
    }
}
