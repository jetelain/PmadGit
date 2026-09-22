using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Pmad.Git.LocalRepositories.Diff;

/// <summary>
/// Represents line insertion and deletion statistics for a diff or commit.
/// </summary>
public sealed record GitDiffStat(int FilesChanged, int Insertions, int Deletions)
{
    private static readonly Regex ShortStatFilesRegex = new(@"(\d+)\s+file", RegexOptions.Compiled);
    private static readonly Regex ShortStatInsertionsRegex = new(@"(\d+)\s+insertion", RegexOptions.Compiled);
    private static readonly Regex ShortStatDeletionsRegex = new(@"(\d+)\s+deletion", RegexOptions.Compiled);

    /// <summary>
    /// Formats the statistics as standard Git shortstat output (e.g. "2 files changed, 5 insertions(+), 1 deletion(-)").
    /// </summary>
    public string ToShortStat()
    {
        if (FilesChanged == 0)
        {
            return string.Empty;
        }

        var filesText = FilesChanged == 1 ? "1 file changed" : $"{FilesChanged} files changed";
        var parts = new List<string>(3) { filesText };

        if (Insertions > 0)
        {
            parts.Add(Insertions == 1 ? "1 insertion(+)" : $"{Insertions} insertions(+)");
        }

        if (Deletions > 0)
        {
            parts.Add(Deletions == 1 ? "1 deletion(-)" : $"{Deletions} deletions(-)");
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Parses the output of <c>git diff --shortstat</c> or <c>git show --shortstat</c>.
    /// </summary>
    /// <param name="output">Raw text output from git.</param>
    /// <returns>A populated <see cref="GitDiffStat"/> instance.</returns>
    public static GitDiffStat ParseShortStat(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return new GitDiffStat(0, 0, 0);
        }

        var filesMatch = ShortStatFilesRegex.Match(output);
        var insertionsMatch = ShortStatInsertionsRegex.Match(output);
        var deletionsMatch = ShortStatDeletionsRegex.Match(output);

        var files = filesMatch.Success && int.TryParse(filesMatch.Groups[1].Value, out var f) ? f : 0;
        var insertions = insertionsMatch.Success && int.TryParse(insertionsMatch.Groups[1].Value, out var i) ? i : 0;
        var deletions = deletionsMatch.Success && int.TryParse(deletionsMatch.Groups[1].Value, out var d) ? d : 0;

        return new GitDiffStat(files, insertions, deletions);
    }
}

