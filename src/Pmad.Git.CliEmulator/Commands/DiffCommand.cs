using System.CommandLine;
using Pmad.Git.CliEmulator.Internal;

namespace Pmad.Git.CliEmulator.Commands;

internal static class DiffCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("diff") { Description = "Show changes between commits, commit and working tree, etc." };

        var stagedOpt = new Option<bool>("--staged", "--cached") { Description = "Compare staged changes against HEAD" };
        var pathOpt = new Option<string?>("--path") { Description = "Limit diff to path" };
        var fromArg = new Argument<string?>("from") { Description = "Base commit/ref", Arity = ArgumentArity.ZeroOrOne };
        var toArg = new Argument<string?>("to") { Description = "Target commit/ref", Arity = ArgumentArity.ZeroOrOne };

        cmd.Options.Add(stagedOpt);
        cmd.Options.Add(pathOpt);
        cmd.Arguments.Add(fromArg);
        cmd.Arguments.Add(toArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var staged = pr.GetValue(stagedOpt);
            var from = pr.GetValue(fromArg);
            var to = pr.GetValue(toArg);
            var path = pr.GetValue(pathOpt);

            try
            {
                string diff;
                if (staged)
                {
                    diff = await ctx.Repository.GetStagedDiffAsync(path, ct);
                }
                else if (from == null && to == null)
                {
                    diff = await ctx.Repository.GetUnstagedDiffAsync(path, ct);
                }
                else
                {
                    diff = await ctx.Repository.GetDiffAsync(from, to, path, ct);
                }
                await ctx.StdOut.WriteAsync(diff);
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
