using System.CommandLine;
using Pmad.Git.CliEmulator.Internal;

namespace Pmad.Git.CliEmulator.Commands;

internal static class AddCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("add") { Description = "Add file contents to the index" };
        var allOpt = new Option<bool>("-A", "--all") { Description = "Stage all changes (modified, deleted, new)" };
        var pathsArg = new Argument<string[]>("paths") { Description = "Files to stage", Arity = ArgumentArity.ZeroOrMore };

        cmd.Options.Add(allOpt);
        cmd.Arguments.Add(pathsArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var all = pr.GetValue(allOpt);
            var paths = pr.GetValue(pathsArg) ?? [];

            try
            {
                if (all || (paths.Length == 1 && paths[0] == "."))
                {
                    await ctx.Repository.StageAllAsync(ct);
                }
                else if (paths.Length == 0)
                {
                    return ctx.WriteError("Nothing specified, nothing added. Use 'git add -A' to stage all.");
                }
                else
                {
                    await ctx.Repository.StageAsync(paths, ct);
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
