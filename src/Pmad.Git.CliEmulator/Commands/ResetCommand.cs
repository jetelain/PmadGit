using System.CommandLine;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Commands;

internal static class ResetCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("reset") { Description = "Reset current HEAD to the specified state" };
        var softOpt = new Option<bool>("--soft") { Description = "Only move HEAD; keep index and working tree" };
        var mixedOpt = new Option<bool>("--mixed") { Description = "Reset index but not working tree (default)" };
        var hardOpt = new Option<bool>("--hard") { Description = "Reset index and working tree" };
        var argsArg = new Argument<string[]>("args") { Description = "Commit and/or paths to reset", Arity = ArgumentArity.ZeroOrMore };

        cmd.Options.Add(softOpt);
        cmd.Options.Add(mixedOpt);
        cmd.Options.Add(hardOpt);
        cmd.Arguments.Add(argsArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var soft = pr.GetValue(softOpt);
            var hard = pr.GetValue(hardOpt);
            var rawArgs = pr.GetValue(argsArg) ?? [];
            var mode = hard ? GitResetMode.Hard : soft ? GitResetMode.Soft : GitResetMode.Mixed;

            try
            {
                var hasDoubleDash = pr.Tokens.Any(t => t.Value == "--");
                string? commitRef = null;
                var paths = new List<string>();

                if (hasDoubleDash)
                {
                    var afterDash = false;
                    foreach (var token in pr.Tokens)
                    {
                        if (token.Value == "--")
                        {
                            afterDash = true;
                            continue;
                        }
                        if (afterDash)
                        {
                            paths.Add(token.Value);
                        }
                        else if (!token.Value.StartsWith('-') && commitRef == null)
                        {
                            commitRef = token.Value;
                        }
                    }
                }
                else if (rawArgs.Length > 0)
                {
                    bool firstIsCommit = false;
                    try
                    {
                        await ctx.Repository.GetCommitAsync(rawArgs[0], ct).ConfigureAwait(false);
                        firstIsCommit = true;
                    }
                    catch
                    {
                        firstIsCommit = false;
                    }

                    if (firstIsCommit)
                    {
                        commitRef = rawArgs[0];
                        if (rawArgs.Length > 1)
                        {
                            paths.AddRange(rawArgs.Skip(1));
                        }
                    }
                    else
                    {
                        paths.AddRange(rawArgs);
                    }
                }

                if (paths.Count > 0)
                {
                    if (soft || hard)
                    {
                        return ctx.WriteError("Cannot do --soft or --hard with paths.");
                    }
                    await ctx.Repository.RestoreIndexAsync(paths, commitRef, ct).ConfigureAwait(false);
                    return 0;
                }

                commitRef ??= "HEAD";
                var targetCommit = await ctx.Repository.GetCommitAsync(commitRef, ct).ConfigureAwait(false);
                var branch = await ctx.Repository.GetCurrentBranchNameAsync(ct).ConfigureAwait(false) ?? "HEAD";
                var headCommit = await ctx.Repository.GetCommitAsync(cancellationToken: ct).ConfigureAwait(false);

                if (!headCommit.Id.Equals(targetCommit.Id))
                {
                    var lost = await ApprovalHelper.CollectLostCommitsAsync(ctx.Repository, headCommit.Id, targetCommit.Id, ct).ConfigureAwait(false);
                    if (lost.Count > 0)
                    {
                        await ApprovalHelper.RequireAsync(
                            new UnpushedCommitLossContext
                            {
                                Operation = $"reset {(hard ? "--hard" : soft ? "--soft" : "--mixed")}",
                                BranchName = branch,
                                CommitsToLose = lost,
                            },
                            ctx.Approval.ApproveUnpushedCommitLossAsync, 
                            ct).ConfigureAwait(false);
                    }
                }

                if (hard)
                {
                    var status = await ctx.Repository.GetStatusAsync(includeUntracked: false, cancellationToken: ct).ConfigureAwait(false);
                    var changedFiles = status.Entries
                        .Where(e => !e.IsClean)
                        .Select(e => e.Path)
                        .ToList();

                    var collisions = await ApprovalHelper.GetUntrackedCollisionsAsync(ctx.Repository, targetCommit, ct).ConfigureAwait(false);
                    foreach (var c in collisions)
                    {
                        if (!changedFiles.Contains(c, StringComparer.OrdinalIgnoreCase))
                        {
                            changedFiles.Add(c);
                        }
                    }

                    if (changedFiles.Count > 0)
                    {
                        await ApprovalHelper.RequireAsync(
                            new DiscardChangesContext
                            {
                                Operation = "reset --hard",
                                AffectedFiles = changedFiles,
                            },
                            ctx.Approval.ApproveDiscardLocalChangesAsync, 
                            ct).ConfigureAwait(false);
                    }
                }

                await ctx.Repository.ResetAsync(targetCommit.Id, mode, ct).ConfigureAwait(false);
                await ctx.StdOut.WriteLineAsync($"HEAD is now at {targetCommit.Id.ToString()[..7]} {targetCommit.Message.Split('\n', 2)[0].Trim()}").ConfigureAwait(false);
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
