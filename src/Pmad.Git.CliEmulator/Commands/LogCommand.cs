using System.CommandLine;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.CliEmulator.Internal.Formatters;

namespace Pmad.Git.CliEmulator.Commands;

internal static class LogCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("log") { Description = "Show commit logs" };

        var onelineOpt = new Option<bool>("--oneline") { Description = "Abbreviate each commit to a single line" };
        var maxCountOpt = new Option<int>("-n", "--max-count")
        {
            Description = "Maximum number of commits to show",
            DefaultValueFactory = _ => 100
        };
        var refArg = new Argument<string?>("ref")
        {
            Description = "Starting commit or branch",
            Arity = ArgumentArity.ZeroOrOne
        };

        cmd.Options.Add(onelineOpt);
        cmd.Options.Add(maxCountOpt);
        cmd.Arguments.Add(refArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var oneline = pr.GetValue(onelineOpt);
            var maxCount = pr.GetValue(maxCountOpt);
            var startRef = pr.GetValue(refArg);

            try
            {
                var commits = ctx.Repository
                    .EnumerateCommitsAsync(startRef, ct)
                    .Take(maxCount, ct);

                if (oneline)
                {
                    await LogFormatter.WriteOnelineAsync(commits, ctx.StdOut, ct);
                }
                else
                {
                    await LogFormatter.WriteFullAsync(commits, ctx.StdOut, ct);
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
