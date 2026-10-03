using System.CommandLine;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Commands;

internal static class CommitCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("commit") { Description = "Record changes to the repository" };
        var messageOpt = new Option<string?>("-m", "--message") { Description = "Commit message" };
        var allOpt = new Option<bool>("-a", "--all") { Description = "Stage all tracked modified/deleted files before committing" };
        var amendOpt = new Option<bool>("--amend") { Description = "Amend the last commit" };
        var allowEmptyOpt = new Option<bool>("--allow-empty") { Description = "Allow recording an empty commit" };

        cmd.Options.Add(messageOpt);
        cmd.Options.Add(allOpt);
        cmd.Options.Add(amendOpt);
        cmd.Options.Add(allowEmptyOpt);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var message = pr.GetValue(messageOpt);
            var all = pr.GetValue(allOpt);
            var amend = pr.GetValue(amendOpt);
            var allowEmpty = pr.GetValue(allowEmptyOpt);

            try
            {
                if (!amend && !allowEmpty)
                {
                    var status = await ctx.Repository.GetStatusAsync(includeUntracked: true, cancellationToken: ct);
                    var hasStaged = status.StagedEntries.Count > 0 || status.ConflictedEntries.Count > 0;
                    var hasTrackedWorktree = all && (status.ModifiedEntries.Count > 0 || status.DeletedEntries.Count > 0);
                    if (!hasStaged && !hasTrackedWorktree)
                    {
                        var hasUntracked = status.UntrackedEntries.Count > 0;
                        if (hasUntracked)
                        {
                            return ctx.WriteError("nothing added to commit but untracked files present");
                        }
                        return ctx.WriteError("nothing to commit, working tree clean");
                    }
                }

                if (amend)
                {
                    var headCommit = await ctx.Repository.GetCommitAsync(cancellationToken: ct);
                    var isPushed = await ctx.Repository.IsCommitPushedAsync(headCommit.Id, cancellationToken: ct);
                    if (isPushed)
                    {
                        var branch = await ctx.Repository.GetCurrentBranchNameAsync(ct) ?? "HEAD";
                        await ApprovalHelper.RequireAsync(
                            new HistoryRewriteContext
                            {
                                Operation = "commit --amend",
                                BranchName = branch,
                                AffectedCommits = [ApprovalHelper.ToSummary(headCommit)],
                                InvolvesRemotePush = false,
                            }, 
                            ctx.Approval.ApproveHistoryRewriteAsync, 
                            ct);
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
