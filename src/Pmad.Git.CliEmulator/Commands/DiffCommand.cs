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
        var argsArg = new Argument<string[]>("args") { Description = "Commits, ranges (A..B), or file paths", Arity = ArgumentArity.ZeroOrMore };

        cmd.Options.Add(stagedOpt);
        cmd.Options.Add(pathOpt);
        cmd.Arguments.Add(argsArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var staged = pr.GetValue(stagedOpt);
            var path = pr.GetValue(pathOpt);
            var rawArgs = pr.GetValue(argsArg) ?? [];

            string? from = null;
            string? to = null;

            var hasDoubleDash = pr.Tokens.Any(t => t.Value == "--");
            if (hasDoubleDash)
            {
                var afterDash = false;
                var dashPaths = new List<string>();
                var beforeArgs = new List<string>();
                foreach (var token in pr.Tokens)
                {
                    if (token.Value == "--")
                    {
                        afterDash = true;
                        continue;
                    }
                    if (afterDash)
                    {
                        dashPaths.Add(token.Value);
                    }
                    else if (!token.Value.StartsWith('-'))
                    {
                        beforeArgs.Add(token.Value);
                    }
                }
                if (dashPaths.Count > 0 && path == null)
                {
                    path = dashPaths[0];
                }
                rawArgs = beforeArgs.ToArray();
            }

            if (rawArgs.Length > 0)
            {
                var first = rawArgs[0];
                if (first.Contains(".."))
                {
                    var parts = first.Split("..", 2);
                    from = string.IsNullOrEmpty(parts[0]) ? "HEAD" : parts[0];
                    to = string.IsNullOrEmpty(parts[1]) ? "HEAD" : parts[1];
                    if (rawArgs.Length > 1 && path == null)
                    {
                        path = rawArgs[1];
                    }
                }
                else
                {
                    bool firstIsCommit = false;
                    try
                    {
                        await ctx.Repository.GetCommitAsync(first, ct).ConfigureAwait(false);
                        firstIsCommit = true;
                    }
                    catch
                    {
                        firstIsCommit = false;
                    }

                    if (firstIsCommit)
                    {
                        from = first;
                        if (rawArgs.Length > 1)
                        {
                            var second = rawArgs[1];
                            bool secondIsCommit = false;
                            try
                            {
                                await ctx.Repository.GetCommitAsync(second, ct).ConfigureAwait(false);
                                secondIsCommit = true;
                            }
                            catch
                            {
                                secondIsCommit = false;
                            }

                            if (secondIsCommit)
                            {
                                to = second;
                                if (rawArgs.Length > 2 && path == null)
                                {
                                    path = rawArgs[2];
                                }
                            }
                            else if (path == null)
                            {
                                path = second;
                            }
                        }
                    }
                    else if (path == null)
                    {
                        path = first;
                    }
                }
            }

            try
            {
                string diff;
                if (staged)
                {
                    diff = await ctx.Repository.GetStagedDiffAsync(path, ct).ConfigureAwait(false);
                }
                else if (from == null && to == null)
                {
                    diff = await ctx.Repository.GetUnstagedDiffAsync(path, ct).ConfigureAwait(false);
                }
                else if (from != null && to == null)
                {
                    diff = await ctx.Repository.GetWorktreeDiffAsync(from, path, ct).ConfigureAwait(false);
                }
                else
                {
                    diff = await ctx.Repository.GetDiffAsync(from, to, path, ct).ConfigureAwait(false);
                }
                await ctx.StdOut.WriteAsync(diff).ConfigureAwait(false);
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
