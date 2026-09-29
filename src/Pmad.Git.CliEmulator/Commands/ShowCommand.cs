using System.CommandLine;
using System.Text;
using Pmad.Git.CliEmulator.Internal;

namespace Pmad.Git.CliEmulator.Commands;

internal static class ShowCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("show") { Description = "Show various types of objects" };
        var objArg = new Argument<string>("object") { Description = "Commit/ref, or ref:path to show blob content" };
        cmd.Arguments.Add(objArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var obj = pr.GetValue(objArg)!;
            try
            {
                var colonIdx = obj.IndexOf(':');
                if (colonIdx > 0)
                {
                    var refPart = obj[..colonIdx];
                    var pathPart = obj[(colonIdx + 1)..];
                    var content = await ctx.Repository.ReadFileAsync(pathPart, refPart, ct);
                    await ctx.StdOut.WriteAsync(Encoding.UTF8.GetString(content));
                }
                else
                {
                    var commit = await ctx.Repository.GetCommitAsync(obj, ct);
                    var meta = commit.Metadata;
                    await ctx.StdOut.WriteLineAsync($"commit {commit.Id}");
                    await ctx.StdOut.WriteLineAsync($"Author: {meta.AuthorName} <{meta.AuthorEmail}>");
                    await ctx.StdOut.WriteLineAsync($"Date:   {meta.AuthorDate:ddd MMM d HH:mm:ss yyyy K}");
                    await ctx.StdOut.WriteLineAsync();
                    foreach (var line in commit.Message.Split('\n'))
                    {
                        await ctx.StdOut.WriteLineAsync($"    {line}");
                    }
                    await ctx.StdOut.WriteLineAsync();
                    var diff = await ctx.Repository.GetCommitDiffAsync(obj, cancellationToken: ct);
                    await ctx.StdOut.WriteAsync(diff);
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
