using System.CommandLine;
using Pmad.Git.CliEmulator.Internal;

namespace Pmad.Git.CliEmulator.Commands;

internal static class RevParseCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("rev-parse") { Description = "Pick out and massage parameters" };
        var refArg = new Argument<string>("ref") { Description = "Reference or object name to resolve" };
        cmd.Arguments.Add(refArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var refName = pr.GetValue(refArg)!;
            try
            {
                var commit = await ctx.Repository.GetCommitAsync(refName, ct);
                await ctx.StdOut.WriteLineAsync(commit.Id.ToString());
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
