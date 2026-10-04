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
        var allMessageOpt = new Option<string?>("-am") { Description = "Stage all tracked modified/deleted files and commit with message" };
        var amendOpt = new Option<bool>("--amend") { Description = "Amend the last commit" };
        var allowEmptyOpt = new Option<bool>("--allow-empty") { Description = "Allow recording an empty commit" };
        var noEditOpt = new Option<bool>("--no-edit") { Description = "Use the selected commit message without editing" };

        cmd.Options.Add(messageOpt);
        cmd.Options.Add(allOpt);
        cmd.Options.Add(allMessageOpt);
        cmd.Options.Add(amendOpt);
        cmd.Options.Add(allowEmptyOpt);
        cmd.Options.Add(noEditOpt);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var message = pr.GetValue(messageOpt) ?? pr.GetValue(allMessageOpt);
            var all = pr.GetValue(allOpt) || pr.GetValue(allMessageOpt) != null;
            var amend = pr.GetValue(amendOpt);
            var allowEmpty = pr.GetValue(allowEmptyOpt);
            var noEdit = pr.GetValue(noEditOpt);

            try
            {
                if (!amend && !allowEmpty)
                {
                    var status = await ctx.Repository.GetStatusAsync(includeUntracked: true, cancellationToken: ct);
                    var hasStaged = status.StagedEntries.Count > 0 || status.ConflictedEntries.Count > 0;
                    var hasTrackedWorktree = all && (status.ModifiedEntries.Count > 0 || status.DeletedEntries.Count > 0);
                    if (!hasStaged && !hasTrackedWorktree)
                    {
                        var hasModifiedOrDeleted = status.ModifiedEntries.Count > 0 || status.DeletedEntries.Count > 0;
                        var hasUntracked = status.UntrackedEntries.Count > 0;
                        if (hasModifiedOrDeleted || hasUntracked)
                        {
                            return ctx.WriteError("nothing to commit (working tree has unstaged changes). Did you forget 'git add <files>' or 'git commit -a'?");
                        }
                        return ctx.WriteError("nothing to commit, working tree clean");
                    }
                }

                if (all)
                {
                    var status = await ctx.Repository.GetStatusAsync(includeUntracked: false, cancellationToken: ct);
                    var trackedDirty = status.Entries
                        .Where(e => e.WorkingTreeStatus == GitFileStatus.Modified || e.WorkingTreeStatus == GitFileStatus.Deleted || e.IsConflicted)
                        .Select(e => e.Path)
                        .ToList();
                    if (trackedDirty.Count > 0)
                    {
                        await ctx.Repository.StageAsync(trackedDirty, ct);
                    }
                }

                if (amend)
                {
                    var headCommit = await ctx.Repository.GetCommitAsync(cancellationToken: ct);
                    var isPushed = await ctx.Repository.IsCommitPushedAsync(headCommit.Id, cancellationToken: ct);
                    if (isPushed)
                    {
                        var branch = await ctx.Repository.GetCurrentBranchNameAsync(allowUnborn: true, ct) ?? "HEAD";
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

                    var effectiveMessage = message ?? headCommit.Message;
                    var hash = await ctx.Repository.CommitAmendAsync(message, stageAll: false, cancellationToken: ct);
                    var currentBranch = await ctx.Repository.GetCurrentBranchNameAsync(allowUnborn: true, ct) ?? "HEAD";
                    await ctx.StdOut.WriteLineAsync($"[{currentBranch} (amend) {hash.ToString()[..7]}] {effectiveMessage.Split('\n', 2)[0].Trim()}");
                    return 0;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        return ctx.WriteError("Commit message required. Use -m <message>.");
                    }

                    var hash = await ctx.Repository.CommitAsync(message!, stageAll: false, cancellationToken: ct);
                    var branch = await ctx.Repository.GetCurrentBranchNameAsync(allowUnborn: true, ct) ?? "HEAD";
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
