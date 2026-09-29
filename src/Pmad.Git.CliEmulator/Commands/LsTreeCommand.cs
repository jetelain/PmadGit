using System.CommandLine;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Commands;

internal static class LsTreeCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("ls-tree") { Description = "List the contents of a tree object" };
        var recursiveOpt = new Option<bool>("-r") { Description = "Recurse into sub-trees" };
        var refArg = new Argument<string?>("tree-ish") { Description = "Tree-ish to list", Arity = ArgumentArity.ZeroOrOne };
        var pathArg = new Argument<string?>("path") { Description = "Limit to path", Arity = ArgumentArity.ZeroOrOne };

        cmd.Options.Add(recursiveOpt);
        cmd.Arguments.Add(refArg);
        cmd.Arguments.Add(pathArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var recursive = pr.GetValue(recursiveOpt);
            var treeish = pr.GetValue(refArg);
            var path = pr.GetValue(pathArg);
            try
            {
                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                await foreach (var item in ctx.Repository.EnumerateCommitTreeAsync(treeish, path, searchOption, ct))
                {
                    var kind = item.Entry.Kind == GitTreeEntryKind.Tree ? "tree" : "blob";
                    await ctx.StdOut.WriteLineAsync($"{kind}\t{item.Path}");
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
