using System.CommandLine;
using System.IO;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Commands;

internal static class MvCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("mv") { Description = "Move or rename a file, a directory, or a symlink" };
        var forceOpt = new Option<bool>("-f", "--force") { Description = "Force move/rename even if target exists" };
        var skipOpt = new Option<bool>("-k") { Description = "Skip move/rename errors" };
        var dryRunOpt = new Option<bool>("-n", "--dry-run") { Description = "Dry run" };
        var verboseOpt = new Option<bool>("-v", "--verbose") { Description = "Be verbose" };
        var argsArg = new Argument<string[]>("args") { Description = "Source file(s) and destination", Arity = ArgumentArity.ZeroOrMore };

        cmd.Options.Add(forceOpt);
        cmd.Options.Add(skipOpt);
        cmd.Options.Add(dryRunOpt);
        cmd.Options.Add(verboseOpt);
        cmd.Arguments.Add(argsArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var force = pr.GetValue(forceOpt);
            var skip = pr.GetValue(skipOpt);
            var dryRun = pr.GetValue(dryRunOpt);
            var verbose = pr.GetValue(verboseOpt);
            var args = pr.GetValue(argsArg) ?? [];

            if (args.Length < 2)
            {
                await ctx.StdErr.WriteLineAsync("usage: git mv [-v] [-f] [-n] [-k] <source> <destination>");
                await ctx.StdErr.WriteLineAsync("   or: git mv [-v] [-f] [-n] [-k] <source>... <destination-directory>");
                return 129;
            }

            var sources = args[..^1];
            var destination = args[^1];

            try
            {
                if (force && !dryRun)
                {
                    var plan = await ctx.Repository.MoveAsync(
                        sources,
                        destination,
                        new GitMoveOptions { Force = true, SkipErrors = skip, DryRun = true },
                        ct).ConfigureAwait(false);

                    var overwrittenFiles = new List<string>();
                    foreach (var item in plan.MovedItems)
                    {
                        var targetFull = Path.Combine(ctx.Repository.RootPath, item.DestinationPath);
                        if (File.Exists(targetFull) && !item.SourcePath.Equals(item.DestinationPath, StringComparison.OrdinalIgnoreCase))
                        {
                            overwrittenFiles.Add(item.DestinationPath);
                        }
                    }

                    if (overwrittenFiles.Count > 0)
                    {
                        await ApprovalHelper.RequireAsync(
                            new DiscardChangesContext
                            {
                                Operation = "mv --force",
                                AffectedFiles = overwrittenFiles,
                            },
                            ctx.Approval.ApproveDiscardLocalChangesAsync,
                            ct).ConfigureAwait(false);
                    }
                }

                var options = new GitMoveOptions
                {
                    Force = force,
                    SkipErrors = skip,
                    DryRun = dryRun,
                };

                var result = await ctx.Repository.MoveAsync(sources, destination, options, ct).ConfigureAwait(false);

                if (dryRun)
                {
                    foreach (var item in result.MovedItems)
                    {
                        await ctx.StdOut.WriteLineAsync($"Checking rename of '{item.SourcePath}' to '{item.DestinationPath}'").ConfigureAwait(false);
                        await ctx.StdOut.WriteLineAsync($"Renaming {item.SourcePath} to {item.DestinationPath}").ConfigureAwait(false);
                    }
                }
                else if (verbose)
                {
                    foreach (var item in result.MovedItems)
                    {
                        await ctx.StdOut.WriteLineAsync($"Renaming {item.SourcePath} to {item.DestinationPath}").ConfigureAwait(false);
                    }
                }

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
