using System.CommandLine;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;

namespace Pmad.Git.CliEmulator.Commands;

internal static class CommitCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("commit") { Description = "Record changes to the repository" };
        var messageOpt = new Option<string?>("-m", "--message") { Description = "Commit message" };
        var allOpt = new Option<bool>("-a", "--all") { Description = "Stage all tracked modified/deleted files before committing" };
        var amendOpt = new Option<bool>("--amend") { Description = "Amend the last commit" };

        cmd.Options.Add(messageOpt);
        cmd.Options.Add(allOpt);
        cmd.Options.Add(amendOpt);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var message = pr.GetValue(messageOpt);
            var all = pr.GetValue(allOpt);
            var amend = pr.GetValue(amendOpt);

            try
            {
                if (amend)
                {
                    var headCommit = await ctx.Repository.GetCommitAsync(cancellationToken: ct);
                    var isPushed = await ctx.Repository.IsCommitPushedAsync(headCommit.Id, cancellationToken: ct);
                    if (isPushed)
                    {
                        var branch = await ctx.Repository.GetCurrentBranchNameAsync(ct) ?? "HEAD";
                        await ApprovalHelper.RequireAsync(
                            ctx.Approval.ApproveHistoryRewriteAsync(new HistoryRewriteContext
                            {
                                Operation = "commit --amend",
                                BranchName = branch,
                                AffectedCommits = [ApprovalHelper.ToSummary(headCommit)],
                                InvolvesRemotePush = false,
                            }, ct),
                            "commit --amend");
                    }

                    var hash = await ctx.Repository.CommitAmendAsync(message, stageAll: all, cancellationToken: ct);
                    var currentBranch = await ctx.Repository.GetCurrentBranchNameAsync(ct) ?? "HEAD";
                    await ctx.StdOut.WriteLineAsync($"[{currentBranch} (amend) {hash.ToString()[..7]}] {message ?? "(amended)"}");
                    return 0;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        return ctx.WriteError("Commit message required. Use -m <message>.");
                    }

                    var hash = await ctx.Repository.CommitAsync(message!, stageAll: all, cancellationToken: ct);
                    var branch = await ctx.Repository.GetCurrentBranchNameAsync(ct) ?? "HEAD";
                    await ctx.StdOut.WriteLineAsync($"[{branch} {hash.ToString()[..7]}] {message}");
                    return 0;
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
