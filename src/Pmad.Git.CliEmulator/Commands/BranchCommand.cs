using System.CommandLine;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.CliEmulator.Internal.Formatters;

namespace Pmad.Git.CliEmulator.Commands;

internal static class BranchCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("branch") { Description = "List, create, or delete branches" };
        var allOpt = new Option<bool>("-a") { Description = "List both local and remote-tracking branches" };
        var verboseOpt = new Option<bool>("-v", "--verbose") { Description = "Show sha1 and commit subject" };
        var deleteOpt = new Option<bool>("-d") { Description = "Delete branch (must be merged)" };
        var forceDeleteOpt = new Option<bool>("-D") { Description = "Force delete branch" };
        var moveOpt = new Option<bool>("-m") { Description = "Rename a branch" };
        var nameArg = new Argument<string?>("name") { Description = "Branch name", Arity = ArgumentArity.ZeroOrOne };
        var newNameArg = new Argument<string?>("newname") { Description = "New branch name (rename/create start point)", Arity = ArgumentArity.ZeroOrOne };

        cmd.Options.Add(allOpt);
        cmd.Options.Add(verboseOpt);
        cmd.Options.Add(deleteOpt);
        cmd.Options.Add(forceDeleteOpt);
        cmd.Options.Add(moveOpt);
        cmd.Arguments.Add(nameArg);
        cmd.Arguments.Add(newNameArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var all = pr.GetValue(allOpt);
            var verbose = pr.GetValue(verboseOpt);
            var delete = pr.GetValue(deleteOpt);
            var forceDelete = pr.GetValue(forceDeleteOpt);
            var move = pr.GetValue(moveOpt);
            var name = pr.GetValue(nameArg);
            var newName = pr.GetValue(newNameArg);

            try
            {
                if (delete || forceDelete)
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        return ctx.WriteError("Branch name required for delete.");
                    }

                    var lost = await ApprovalHelper.CollectUnpushedCommitsAsync(ctx.Repository, $"refs/heads/{name}", 50, ct);
                    if (forceDelete || lost.Count > 0)
                    {
                        await ApprovalHelper.RequireAsync(
                            new UnpushedCommitLossContext
                            {
                                Operation = forceDelete ? "branch -D" : "branch -d",
                                BranchName = name,
                                CommitsToLose = lost,
                            },
                            ctx.Approval.ApproveUnpushedCommitLossAsync,
                            ct);
                    }
                    await ctx.Repository.DeleteBranchAsync(name, force: forceDelete, ct);
                    await ctx.StdOut.WriteLineAsync($"Deleted branch {name}.");
                    return 0;
                }

                if (move)
                {
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(newName))
                    {
                        return ctx.WriteError("branch -m requires <old-name> <new-name>.");
                    }
                    await ctx.Repository.RenameBranchAsync(name, newName, ct);
                    await ctx.StdOut.WriteLineAsync($"Renamed branch '{name}' to '{newName}'.");
                    return 0;
                }

                if (!string.IsNullOrEmpty(name))
                {
                    await ctx.Repository.CreateBranchAsync(name, newName, ct);
                    await ctx.StdOut.WriteLineAsync($"Branch '{name}' created.");
                    return 0;
                }

                await BranchFormatter.WriteAsync(ctx.Repository, all, verbose, ctx.StdOut, ct);
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
