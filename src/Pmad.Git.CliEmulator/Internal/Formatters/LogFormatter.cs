using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Internal.Formatters;

internal static class LogFormatter
{
    public static async Task WriteOnelineAsync(
        IAsyncEnumerable<GitCommit> commits,
        TextWriter writer,
        CancellationToken ct)
    {
        await foreach (var commit in commits.WithCancellation(ct).ConfigureAwait(false))
        {
            var hash = commit.Id.ToString();
            var shortHash = hash.Length >= 7 ? hash[..7] : hash;
            var subject = commit.Message.Split('\n', 2)[0].Trim();
            await writer.WriteLineAsync($"{shortHash} {subject}").ConfigureAwait(false);
        }
    }

    public static async Task WriteFullAsync(
        IAsyncEnumerable<GitCommit> commits,
        TextWriter writer,
        CancellationToken ct)
    {
        var first = true;
        await foreach (var commit in commits.WithCancellation(ct).ConfigureAwait(false))
        {
            if (!first) await writer.WriteLineAsync().ConfigureAwait(false);
            first = false;

            var meta = commit.Metadata;
            await writer.WriteLineAsync($"commit {commit.Id}").ConfigureAwait(false);
            await writer.WriteLineAsync($"Author: {meta.AuthorName} <{meta.AuthorEmail}>").ConfigureAwait(false);
            await writer.WriteLineAsync($"Date:   {meta.AuthorDate:ddd MMM d HH:mm:ss yyyy K}").ConfigureAwait(false);
            await writer.WriteLineAsync().ConfigureAwait(false);
            foreach (var line in commit.Message.Split('\n'))
            {
                await writer.WriteLineAsync($"    {line}").ConfigureAwait(false);
            }
        }
    }
}
