using System.CommandLine;
using System.Runtime.CompilerServices;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.CliEmulator.Internal.Formatters;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Commands;

internal static class LogCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("log") { Description = "Show commit logs" };

        var onelineOpt = new Option<bool>("--oneline") { Description = "Abbreviate each commit to a single line" };
        var maxCountOpt = new Option<int?>("-n", "--max-count")
        {
            Description = "Maximum number of commits to show"
        };
        var refArg = new Argument<string?>("ref")
        {
            Description = "Starting commit or branch",
            Arity = ArgumentArity.ZeroOrOne
        };

        cmd.Options.Add(onelineOpt);
        cmd.Options.Add(maxCountOpt);
        cmd.Arguments.Add(refArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var oneline = pr.GetValue(onelineOpt);
            var maxCount = pr.GetValue(maxCountOpt);
            var startRef = pr.GetValue(refArg);

            try
            {
                IAsyncEnumerable<GitCommit> commits;
                if (startRef != null && startRef.Contains(".."))
                {
                    var parts = startRef.Split("..", 2);
                    var excludeRef = string.IsNullOrEmpty(parts[0]) ? "HEAD" : parts[0];
                    var includeRef = string.IsNullOrEmpty(parts[1]) ? "HEAD" : parts[1];

                    var excludedHashes = new HashSet<GitHash>();
                    await foreach (var c in ctx.Repository.EnumerateCommitsAsync(excludeRef, ct).ConfigureAwait(false))
                    {
                        excludedHashes.Add(c.Id);
                    }

                    commits = FilterCommitsAsync(ctx.Repository.EnumerateCommitsAsync(includeRef, ct), excludedHashes, ct);
                }
                else
                {
                    commits = ctx.Repository.EnumerateCommitsAsync(startRef, ct);
                }

                if (maxCount.HasValue && maxCount.Value >= 0)
                {
                    commits = commits.Take(maxCount.Value, ct);
                }

                if (oneline)
                {
                    await LogFormatter.WriteOnelineAsync(commits, ctx.StdOut, ct);
                }
                else
                {
                    await LogFormatter.WriteFullAsync(commits, ctx.StdOut, ct);
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

    private static async IAsyncEnumerable<GitCommit> FilterCommitsAsync(
        IAsyncEnumerable<GitCommit> source,
        HashSet<GitHash> excluded,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await foreach (var c in source.WithCancellation(ct).ConfigureAwait(false))
        {
            if (!excluded.Contains(c.Id))
            {
                yield return c;
            }
        }
    }
}
