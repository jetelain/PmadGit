using System.CommandLine;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories.Config;

namespace Pmad.Git.CliEmulator.Commands;

internal static class RemoteCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("remote") { Description = "Manage set of tracked repositories" };
        var verboseOpt = new Option<bool>("-v", "--verbose") { Description = "Show remote URL after name" };
        cmd.Options.Add(verboseOpt);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var verbose = pr.GetValue(verboseOpt);
            try
            {
                var configPath = Path.Combine(ctx.Repository.GitDirectory, "config");
                var config = await GitConfigFile.ReadFromFileAsync(configPath, ct);
                var remotes = config.GetSubsections("remote");
                foreach (var remote in remotes)
                {
                    if (verbose)
                    {
                        var url = config.GetValue("remote", remote, "url") ?? "(no url)";
                        await ctx.StdOut.WriteLineAsync($"{remote}\t{url} (fetch)");
                        await ctx.StdOut.WriteLineAsync($"{remote}\t{url} (push)");
                    }
                    else
                    {
                        await ctx.StdOut.WriteLineAsync(remote);
                    }
                }
                return 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not GitCliDeniedException)
            {
                return ctx.WriteError(ex.Message);
            }
        });
        return cmd;
    }
}
