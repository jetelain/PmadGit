using System.CommandLine;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Commands;

internal static class AddCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("add") { Description = "Add file contents to the index" };
        var allOpt = new Option<bool>("-A", "--all") { Description = "Stage all changes (modified, deleted, new)" };
        var updateOpt = new Option<bool>("-u", "--update") { Description = "Update the index just where it already has an entry matching <pathspec>" };
        var pathsArg = new Argument<string[]>("paths") { Description = "Files to stage", Arity = ArgumentArity.ZeroOrMore };

        cmd.Options.Add(allOpt);
        cmd.Options.Add(updateOpt);
        cmd.Arguments.Add(pathsArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var all = pr.GetValue(allOpt);
            var update = pr.GetValue(updateOpt);
            var paths = pr.GetValue(pathsArg) ?? [];

            try
            {
                if (update)
                {
                    var status = await ctx.Repository.GetStatusAsync(includeUntracked: false, cancellationToken: ct);
                    var trackedDirty = status.Entries
                        .Where(e => e.WorkingTreeStatus == GitFileStatus.Modified || e.WorkingTreeStatus == GitFileStatus.Deleted || e.IsConflicted)
                        .Select(e => e.Path)
                        .ToList();

                    if (paths.Length == 0 || paths.Contains("."))
                    {
                        if (trackedDirty.Count > 0)
                        {
                            await ctx.Repository.StageAsync(trackedDirty, ct);
                        }
                        return 0;
                    }

                    var pathsToStage = new List<string>();
                    foreach (var p in paths)
                    {
                        var normalizedP = p.Replace('\\', '/').Trim('/');
                        var matched = trackedDirty.Where(td => td.Equals(normalizedP, StringComparison.OrdinalIgnoreCase) ||
                                                               td.StartsWith(normalizedP + "/", StringComparison.OrdinalIgnoreCase) ||
                                                               MatchesPattern(p, td)).ToList();
                        pathsToStage.AddRange(matched);
                    }
                    var distinctPaths = pathsToStage.Distinct(StringComparer.Ordinal).ToList();
                    if (distinctPaths.Count > 0)
                    {
                        await ctx.Repository.StageAsync(distinctPaths, ct);
                    }
                    return 0;
                }

                if (all || paths.Contains("."))
                {
                    await ctx.Repository.StageAllAsync(ct);
                    return 0;
                }
                if (paths.Length == 0)
                {
                    return ctx.WriteError("Nothing specified, nothing added. Use 'git add -A' to stage all.");
                }

                var index = await GitIndex.ReadAsync(ctx.Repository.IndexManager.IndexPath, ctx.Repository.HashLengthBytes, ct);
                var hasWildcards = paths.Any(p => p.Contains('*') || p.Contains('?'));
                if (hasWildcards)
                {
                    var status = await ctx.Repository.GetStatusAsync(includeUntracked: true, cancellationToken: ct);
                    var candidatePaths = status.Entries
                        .Where(e => e.HasWorkingTreeChanges || e.StagedStatus == GitFileStatus.Untracked || e.IsConflicted)
                        .Select(e => e.Path)
                        .ToList();

                    var expanded = new List<string>();
                    foreach (var p in paths)
                    {
                        if (p.Contains('*') || p.Contains('?'))
                        {
                            var matched = candidatePaths.Where(c => MatchesPattern(p, c)).ToList();
                            expanded.AddRange(matched);
                        }
                        else
                        {
                            var normalized = p.Replace('\\', '/').Trim('/');
                            var fullPath = Path.Combine(ctx.Repository.RootPath, p.Replace('/', Path.DirectorySeparatorChar));
                            var existsOnDisk = File.Exists(fullPath) || Directory.Exists(fullPath);
                            var existsInIndex = index.FindEntry(normalized) != null || index.Entries.Any(e => e.Path.StartsWith(normalized + "/", StringComparison.Ordinal));
                            if (!existsOnDisk && !existsInIndex)
                            {
                                return ctx.WriteFatal($"pathspec '{p}' did not match any files");
                            }
                            expanded.Add(p);
                        }
                    }
                    paths = expanded.Distinct(StringComparer.Ordinal).ToArray();
                }
                else
                {
                    foreach (var p in paths)
                    {
                        var normalized = p.Replace('\\', '/').Trim('/');
                        var fullPath = Path.Combine(ctx.Repository.RootPath, p.Replace('/', Path.DirectorySeparatorChar));
                        var existsOnDisk = File.Exists(fullPath) || Directory.Exists(fullPath);
                        var existsInIndex = index.FindEntry(normalized) != null || index.Entries.Any(e => e.Path.StartsWith(normalized + "/", StringComparison.Ordinal));
                        if (!existsOnDisk && !existsInIndex)
                        {
                            return ctx.WriteFatal($"pathspec '{p}' did not match any files");
                        }
                    }
                }

                await ctx.Repository.StageAsync(paths, ct);
                return 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not GitCliDeniedException)
            {
                return ctx.WriteError(ex.Message);
            }
        });
        return cmd;
    }

    private static bool MatchesPattern(string pattern, string path)
    {
        var normalizedPattern = pattern.Replace('\\', '/');
        var normalizedPath = path.Replace('\\', '/');

        if (!normalizedPattern.Contains('/'))
        {
            var fileName = Path.GetFileName(normalizedPath);
            return System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(normalizedPattern, fileName, ignoreCase: OperatingSystem.IsWindows());
        }

        var patternParts = normalizedPattern.Split('/');
        var pathParts = normalizedPath.Split('/');
        if (patternParts.Length != pathParts.Length)
        {
            return false;
        }

        for (int i = 0; i < patternParts.Length; i++)
        {
            if (!System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(patternParts[i], pathParts[i], ignoreCase: OperatingSystem.IsWindows()))
            {
                return false;
            }
        }
        return true;
    }
}
