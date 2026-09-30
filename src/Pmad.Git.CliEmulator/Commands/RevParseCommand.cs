using System.CommandLine;
using Pmad.Git.CliEmulator.Internal;

namespace Pmad.Git.CliEmulator.Commands;

internal static class RevParseCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("rev-parse") { Description = "Pick out and massage parameters" };
        var showToplevelOpt = new Option<bool>("--show-toplevel") { Description = "Show the working tree root directory" };
        var gitDirOpt = new Option<bool>("--git-dir") { Description = "Show the .git directory" };
        var isInsideWorkTreeOpt = new Option<bool>("--is-inside-work-tree") { Description = "Check if inside work tree" };
        var refArg = new Argument<string?>("ref") { Description = "Reference or object name to resolve", Arity = ArgumentArity.ZeroOrOne };

        cmd.Options.Add(showToplevelOpt);
        cmd.Options.Add(gitDirOpt);
        cmd.Options.Add(isInsideWorkTreeOpt);
        cmd.Arguments.Add(refArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            if (pr.GetValue(showToplevelOpt))
            {
                await ctx.StdOut.WriteLineAsync(ctx.Repository.RootPath);
                return 0;
            }
            if (pr.GetValue(gitDirOpt))
            {
                await ctx.StdOut.WriteLineAsync(ctx.Repository.GitDirectory);
                return 0;
            }
            if (pr.GetValue(isInsideWorkTreeOpt))
            {
                await ctx.StdOut.WriteLineAsync("true");
                return 0;
            }

            var refName = pr.GetValue(refArg);
            if (string.IsNullOrEmpty(refName))
            {
                return ctx.WriteFatal("Reference required.");
            }

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
