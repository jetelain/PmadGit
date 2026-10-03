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
        var pathsArg = new Argument<string[]>("paths") { Description = "Files to stage", Arity = ArgumentArity.ZeroOrMore };

        cmd.Options.Add(allOpt);
        cmd.Arguments.Add(pathsArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var all = pr.GetValue(allOpt);
            var paths = pr.GetValue(pathsArg) ?? [];

            try
            {
                if (all || paths.Contains("."))
                {
                    await ctx.Repository.StageAllAsync(ct);
                    return 0;
                }
                if (paths.Length == 0)
                {
                    return ctx.WriteError("Nothing specified, nothing added. Use 'git add -A' to stage all.");
                }

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
                            expanded.Add(p);
                        }
                    }
                    paths = expanded.Distinct(StringComparer.Ordinal).ToArray();
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
