using System.CommandLine;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories.Config;

namespace Pmad.Git.CliEmulator.Commands;

internal static class FetchCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("fetch") { Description = "Download objects and refs from another repository" };
        var pruneOpt = new Option<bool>("--prune", "-p") { Description = "Remove stale remote-tracking refs" };
        var remoteArg = new Argument<string?>("remote") { Description = "Remote name", Arity = ArgumentArity.ZeroOrOne };
        var branchArg = new Argument<string?>("branch") { Description = "Branch to fetch", Arity = ArgumentArity.ZeroOrOne };

        cmd.Options.Add(pruneOpt);
        cmd.Arguments.Add(remoteArg);
        cmd.Arguments.Add(branchArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            if (!ctx.EnsureRemote(out var remote))
            {
                return 128;
            }
            var prune = pr.GetValue(pruneOpt);
            var remoteName = pr.GetValue(remoteArg) ?? "origin";
            var branch = pr.GetValue(branchArg);

            try
            {
                var remoteUrl = await ResolveRemoteUrlAsync(ctx, remoteName, ct);
                await ApprovalHelper.RequireAsync(
                    ctx.Approval.ApproveReadRemoteAsync(new ReadRemoteContext
                    {
                        Operation = "fetch",
                        RemoteName = remoteName,
                        RemoteUrl = ApprovalHelper.SanitizeUrl(remoteUrl),
                        Branch = branch,
                    }, ct),
                    "fetch");
                await remote.FetchAsync(remoteName, branch, prune, ct);
                await ctx.StdOut.WriteLineAsync("Fetch complete.");
                return 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not GitCliDeniedException)
            {
                return ctx.WriteFatal(ex.Message);
            }
        });
        return cmd;
    }

    internal static async Task<string> ResolveRemoteUrlAsync(CommandContext ctx, string remoteName, CancellationToken ct)
    {
        var configPath = Path.Combine(ctx.Repository.GitDirectory, "config");
        var config = await GitConfigFile.ReadFromFileAsync(configPath, ct).ConfigureAwait(false);
        return config.GetValue("remote", remoteName, "url") ?? remoteName;
    }
}
