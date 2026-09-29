using System.CommandLine;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.CliEmulator.Internal.Formatters;

namespace Pmad.Git.CliEmulator.Commands;

internal static class StatusCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("status") { Description = "Show the working tree status" };
        cmd.SetAction(async (ParseResult _, CancellationToken ct) =>
        {
            try
            {
                await StatusFormatter.WriteAsync(ctx.Repository, ctx.StdOut, ct);
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
