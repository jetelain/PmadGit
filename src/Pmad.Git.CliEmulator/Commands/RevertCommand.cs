using System.CommandLine;
using Pmad.Git.CliEmulator.Internal;

namespace Pmad.Git.CliEmulator.Commands;

internal static class RevertCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("revert") { Description = "Revert some existing commits" };
        var commitArg = new Argument<string>("commit") { Description = "Commit to revert" };
        cmd.Arguments.Add(commitArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var commitRef = pr.GetValue(commitArg)!;
            try
            {
                var commit = await ctx.Repository.GetCommitAsync(commitRef, ct);
                var hash = await ctx.Repository.RevertAsync(commit.Id, cancellationToken: ct);
                var branch = await ctx.Repository.GetCurrentBranchNameAsync(ct) ?? "HEAD";
                await ctx.StdOut.WriteLineAsync($"[{branch} {hash.ToString()[..7]}] Revert \"{commit.Message.Split('\n', 2)[0].Trim()}\"");
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
