using System.CommandLine;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Commands;

internal static class PullCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("pull") { Description = "Fetch from and integrate with another repository or branch" };
        var remoteArg = new Argument<string?>("remote") { Description = "Remote name", Arity = ArgumentArity.ZeroOrOne };
        var branchArg = new Argument<string?>("branch") { Description = "Branch to pull", Arity = ArgumentArity.ZeroOrOne };

        cmd.Arguments.Add(remoteArg);
        cmd.Arguments.Add(branchArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            if (!ctx.EnsureRemote(out var remote))
            {
                return 128;
            }
            var remoteName = pr.GetValue(remoteArg) ?? "origin";
            var branch = pr.GetValue(branchArg);

            try
            {
                var remoteUrl = await FetchCommand.ResolveRemoteUrlAsync(ctx, remoteName, ct);
                await ApprovalHelper.RequireAsync(
                    ctx.Approval.ApproveReadRemoteAsync(new ReadRemoteContext
                    {
                        Operation = "pull",
                        RemoteName = remoteName,
                        RemoteUrl = ApprovalHelper.SanitizeUrl(remoteUrl),
                        Branch = branch,
                    }),
                    "pull");
                var result = await remote.PullAsync(remoteName, branch, rebase: false, ct);
                if (result.IsSuccess)
                {
                    var msg = result.Status == GitMergeStatus.AlreadyUpToDate ? "Already up to date." : "Pull complete.";
                    await ctx.StdOut.WriteLineAsync(msg);
                    return 0;
                }
                await ctx.StdOut.WriteLineAsync("Automatic merge failed; fix conflicts and then commit the result.");
                foreach (var f in result.ConflictedFiles)
                {
                    await ctx.StdErr.WriteLineAsync($"CONFLICT: {f}");
                }
                return 1;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not GitCliDeniedException)
            {
                return ctx.WriteFatal(ex.Message);
            }
        });
        return cmd;
    }
}
