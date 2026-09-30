using System.CommandLine;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.CliEmulator.Internal.Formatters;

namespace Pmad.Git.CliEmulator.Commands;

internal static class StatusCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("status") { Description = "Show the working tree status" };
        var shortOpt = new Option<bool>("-s", "--short") { Description = "Give the output in the short-format" };
        var porcelainOpt = new Option<bool>("--porcelain") { Description = "Give the output in an easy-to-parse format" };

        cmd.Options.Add(shortOpt);
        cmd.Options.Add(porcelainOpt);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var isShort = pr.GetValue(shortOpt) || pr.GetValue(porcelainOpt);
            try
            {
                if (isShort)
                {
                    await StatusFormatter.WriteShortAsync(ctx.Repository, ctx.StdOut, ct);
                }
                else
                {
                    await StatusFormatter.WriteAsync(ctx.Repository, ctx.StdOut, ct);
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
