using System.CommandLine;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Commands;

internal static class RestoreCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("restore") { Description = "Restore working tree files" };
        var stagedOpt = new Option<bool>("-S", "--staged") { Description = "Restore the index (unstage)" };
        var worktreeOpt = new Option<bool>("-W", "--worktree") { Description = "Restore the working tree (default)" };
        var sourceOpt = new Option<string?>("-s", "--source") { Description = "Restore from this tree-ish" };
        var oursOpt = new Option<bool>("--ours") { Description = "Restore our version for unmerged files" };
        var theirsOpt = new Option<bool>("--theirs") { Description = "Restore their version for unmerged files" };
        var pathsArg = new Argument<string[]>("paths") { Description = "Files to restore", Arity = ArgumentArity.OneOrMore };

        cmd.Options.Add(stagedOpt);
        cmd.Options.Add(worktreeOpt);
        cmd.Options.Add(sourceOpt);
        cmd.Options.Add(oursOpt);
        cmd.Options.Add(theirsOpt);
        cmd.Arguments.Add(pathsArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var staged = pr.GetValue(stagedOpt);
            var worktree = pr.GetValue(worktreeOpt);
            var source = pr.GetValue(sourceOpt);
            var isOurs = pr.GetValue(oursOpt);
            var isTheirs = pr.GetValue(theirsOpt);
            var paths = pr.GetValue(pathsArg) ?? [];

            if (paths.Length == 0)
            {
                return ctx.WriteError("No paths specified.");
            }

            if (isOurs || isTheirs)
            {
                var targetStage = isOurs ? 2 : 3;

                await ApprovalHelper.RequireAsync(
                    new DiscardChangesContext
                    {
                        Operation = isOurs ? "restore --ours" : "restore --theirs",
                        AffectedFiles = paths,
                    },
                    ctx.Approval.ApproveDiscardLocalChangesAsync,
                    ct).ConfigureAwait(false);

                var index = await GitIndex.ReadAsync(ctx.Repository.IndexManager.IndexPath, ctx.Repository.HashLengthBytes, ct).ConfigureAwait(false);
                foreach (var path in paths)
                {
                    var normalizedPath = ctx.Repository.IndexManager.NormalizeAndValidateRelativePath(path);
                    var entry = index.FindEntry(normalizedPath, stage: targetStage);
                    if (entry == null)
                    {
                        return ctx.WriteError($"path '{path}' does not have {(isOurs ? "our" : "their")} version");
                    }
                    var obj = await ctx.Repository.ObjectStore.ReadObjectAsync(entry.Hash, ct).ConfigureAwait(false);
                    var fullPath = Path.Combine(ctx.Repository.RootPath, normalizedPath.Replace('/', Path.DirectorySeparatorChar));
                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    await File.WriteAllBytesAsync(fullPath, obj.Content, ct).ConfigureAwait(false);
                }
                return 0;
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
                    if (source != null)
                    {
                        await ctx.Repository.RestoreIndexAsync(paths, source, ct);
                    }
                    else
                    {
                        await ctx.Repository.UnstageAsync(paths, ct);
                    }
                }

                if (targetWorktree)
                {
                    foreach (var path in paths)
                    {
                        await ctx.Repository.RestoreFileAsync(path, source, ct);
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
