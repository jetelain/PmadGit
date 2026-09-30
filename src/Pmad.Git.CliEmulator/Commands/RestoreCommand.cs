using System.CommandLine;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;

namespace Pmad.Git.CliEmulator.Commands;

internal static class RestoreCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("restore") { Description = "Restore working tree files" };
        var stagedOpt = new Option<bool>("--staged") { Description = "Restore the index (unstage)" };
        var worktreeOpt = new Option<bool>("--worktree") { Description = "Restore the working tree (default)" };
        var sourceOpt = new Option<string?>("--source") { Description = "Restore from this tree-ish" };
        var pathsArg = new Argument<string[]>("paths") { Description = "Files to restore", Arity = ArgumentArity.OneOrMore };

        cmd.Options.Add(stagedOpt);
        cmd.Options.Add(worktreeOpt);
        cmd.Options.Add(sourceOpt);
        cmd.Arguments.Add(pathsArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var staged = pr.GetValue(stagedOpt);
            var worktree = pr.GetValue(worktreeOpt);
            var source = pr.GetValue(sourceOpt);
            var paths = pr.GetValue(pathsArg) ?? [];

            if (paths.Length == 0)
            {
                return ctx.WriteError("No paths specified.");
            }

            var targetWorktree = worktree || !staged;
            var targetStaged = staged;

            try
            {
                if (targetWorktree)
                {
                    await ApprovalHelper.RequireAsync(
                        new DiscardChangesContext
                        {
                            Operation = source != null ? $"restore --source {source}" : "restore",
                            AffectedFiles = paths,
                        },
                        ctx.Approval.ApproveDiscardLocalChangesAsync,
                        ct);
                }

                if (targetStaged)
                {
                    await ctx.Repository.UnstageAsync(paths, ct);
                }

                if (targetWorktree)
                {
                    if (source != null)
                    {
                        foreach (var path in paths)
                        {
                            var content = await ctx.Repository.ReadFileAsync(path, source, ct);
                            var fullPath = Path.Combine(ctx.Repository.RootPath, path.Replace('/', Path.DirectorySeparatorChar));
                            var dir = Path.GetDirectoryName(fullPath);
                            if (!string.IsNullOrEmpty(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }
                            await File.WriteAllBytesAsync(fullPath, content, ct);
                        }
                    }
                    else
                    {
                        foreach (var path in paths)
                        {
                            await ctx.Repository.RestoreFileAsync(path, ct);
                        }
                    }
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
