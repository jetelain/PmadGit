using System.CommandLine;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;

namespace Pmad.Git.CliEmulator.Commands;

internal static class PushCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("push") { Description = "Update remote refs along with associated objects" };
        var forceOpt = new Option<bool>("--force", "-f") { Description = "Force push" };
        var setUpstreamOpt = new Option<bool>("-u", "--set-upstream") { Description = "Set upstream tracking reference" };
        var remoteArg = new Argument<string?>("remote") { Description = "Remote name", Arity = ArgumentArity.ZeroOrOne };
        var branchArg = new Argument<string?>("branch") { Description = "Branch to push", Arity = ArgumentArity.ZeroOrOne };

        cmd.Options.Add(forceOpt);
        cmd.Options.Add(setUpstreamOpt);
        cmd.Arguments.Add(remoteArg);
        cmd.Arguments.Add(branchArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            if (!ctx.EnsureRemote(out var remote))
            {
                return 128;
            }
            var force = pr.GetValue(forceOpt);
            var setUpstream = pr.GetValue(setUpstreamOpt);
            var remoteName = pr.GetValue(remoteArg) ?? "origin";
            var branch = pr.GetValue(branchArg);

            try
            {
                var currentBranch = branch ?? await ctx.Repository.GetCurrentBranchNameAsync(ct) ?? "HEAD";
                var tracking = await ctx.Repository.GetTrackingStatusAsync(currentBranch, ct);
                var remoteUrl = await FetchCommand.ResolveRemoteUrlAsync(ctx, remoteName, ct);

                await ApprovalHelper.RequireAsync(
                    ctx.Approval.ApproveWriteRemoteAsync(new WriteRemoteContext
                    {
                        RemoteName = remoteName,
                        RemoteUrl = ApprovalHelper.SanitizeUrl(remoteUrl),
                        BranchName = currentBranch,
                        IsForce = force,
                        CommitsAhead = tracking.AheadCount,
                    }, ct),
                    "push");

                if (force)
                {
                    var headCommit = await ctx.Repository.GetCommitAsync(cancellationToken: ct);
                    await ApprovalHelper.RequireAsync(
                        ctx.Approval.ApproveHistoryRewriteAsync(new HistoryRewriteContext
                        {
                            Operation = "push --force",
                            BranchName = currentBranch,
                            AffectedCommits = [ApprovalHelper.ToSummary(headCommit)],
                            InvolvesRemotePush = true,
                        }, ct),
                        "push --force");
                }

                await remote.PushAsync(remoteName, branch, force, setUpstream, ct);
                await ctx.StdOut.WriteLineAsync($"To {ApprovalHelper.SanitizeUrl(remoteUrl)}");
                await ctx.StdOut.WriteLineAsync($"   {currentBranch} -> {currentBranch}");
                return 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not GitCliDeniedException)
            {
                return ctx.WriteFatal(ex.Message);
            }
        });
        return cmd;
    }
}
