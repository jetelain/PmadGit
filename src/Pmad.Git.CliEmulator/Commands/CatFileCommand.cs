using System.CommandLine;
using System.Text;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Commands;

internal static class CatFileCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("cat-file") { Description = "Provide content or type information for repository objects" };
        var typeOpt = new Option<bool>("-t") { Description = "Show the object type" };
        var printOpt = new Option<bool>("-p") { Description = "Pretty-print object content" };
        var objArg = new Argument<string>("object") { Description = "Object hash or ref" };

        cmd.Options.Add(typeOpt);
        cmd.Options.Add(printOpt);
        cmd.Arguments.Add(objArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var showType = pr.GetValue(typeOpt);
            var print = pr.GetValue(printOpt);
            var obj = pr.GetValue(objArg)!;

            try
            {
                var commit = await ctx.Repository.GetCommitAsync(obj, ct);
                if (showType)
                {
                    await ctx.StdOut.WriteLineAsync("commit");
                }
                else if (print)
                {
                    await ctx.StdOut.WriteLineAsync($"tree {commit.Tree}");
                    foreach (var parent in commit.Parents)
                    {
                        await ctx.StdOut.WriteLineAsync($"parent {parent}");
                    }
                    await ctx.StdOut.WriteLineAsync($"author {commit.Author}");
                    await ctx.StdOut.WriteLineAsync($"committer {commit.Committer}");
                    await ctx.StdOut.WriteLineAsync();
                    await ctx.StdOut.WriteAsync(commit.Message);
                }
                else
                {
                    return ctx.WriteError("Specify -t or -p.");
                }
                return 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not GitCliDeniedException)
            {
                try
                {
                    var hash = new GitHash(obj);
                    var data = await ctx.Repository.ObjectStore.ReadObjectAsync(hash, ct);
                    if (showType)
                    {
                        await ctx.StdOut.WriteLineAsync(data.Type.ToString().ToLowerInvariant());
                    }
                    else if (print)
                    {
                        await ctx.StdOut.WriteAsync(Encoding.UTF8.GetString(data.Content));
                    }
                    else
                    {
                        return ctx.WriteError("Specify -t or -p.");
                    }
                    return 0;
                }
                catch (Exception ex2) when (ex2 is not OperationCanceledException and not GitCliDeniedException)
                {
                    return ctx.WriteFatal(ex2.Message);
                }
            }
        });
        return cmd;
    }
}
