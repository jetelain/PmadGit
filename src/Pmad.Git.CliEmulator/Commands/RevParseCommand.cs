using System.CommandLine;
using Pmad.Git.CliEmulator.Internal;

namespace Pmad.Git.CliEmulator.Commands;

internal static class RevParseCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("rev-parse") { Description = "Pick out and massage parameters" };
        var abbrevRefOpt = new Option<bool>("--abbrev-ref") { Description = "Strict abbreviation mode" };
        var showToplevelOpt = new Option<bool>("--show-toplevel") { Description = "Show the working tree root directory" };
        var gitDirOpt = new Option<bool>("--git-dir") { Description = "Show the .git directory" };
        var isInsideWorkTreeOpt = new Option<bool>("--is-inside-work-tree") { Description = "Check if inside work tree" };
        var refArg = new Argument<string?>("ref") { Description = "Reference or object name to resolve", Arity = ArgumentArity.ZeroOrOne };

        cmd.Options.Add(abbrevRefOpt);
        cmd.Options.Add(showToplevelOpt);
        cmd.Options.Add(gitDirOpt);
        cmd.Options.Add(isInsideWorkTreeOpt);
        cmd.Arguments.Add(refArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            if (pr.GetValue(abbrevRefOpt))
            {
                var target = pr.GetValue(refArg);
                if (string.IsNullOrEmpty(target))
                {
                    return ctx.WriteFatal("Reference required.");
                }

                if (string.Equals(target, "HEAD", StringComparison.OrdinalIgnoreCase))
                {
                    var branch = await ctx.Repository.GetCurrentBranchNameAsync(ct);
                    await ctx.StdOut.WriteLineAsync(branch ?? "HEAD");
                    return 0;
                }

                if (target.Equals("@{u}", StringComparison.OrdinalIgnoreCase) ||
                    target.Equals("@{upstream}", StringComparison.OrdinalIgnoreCase) ||
                    target.Equals("HEAD@{u}", StringComparison.OrdinalIgnoreCase) ||
                    target.Equals("HEAD@{upstream}", StringComparison.OrdinalIgnoreCase))
                {
                    var branch = await ctx.Repository.GetCurrentBranchNameAsync(ct);
                    if (string.IsNullOrEmpty(branch))
                    {
                        return ctx.WriteFatal("HEAD does not point to a branch.");
                    }
                    var tracking = await ctx.Repository.GetTrackingStatusAsync(branch, ct);
                    if (!tracking.HasUpstream)
                    {
                        return ctx.WriteFatal($"No upstream configured for branch '{branch}'.");
                    }
                    await ctx.StdOut.WriteLineAsync(tracking.UpstreamBranch!);
                    return 0;
                }

                if (target.StartsWith("refs/heads/", StringComparison.Ordinal))
                {
                    target = target["refs/heads/".Length..];
                }
                await ctx.StdOut.WriteLineAsync(target);
                return 0;
            }

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
