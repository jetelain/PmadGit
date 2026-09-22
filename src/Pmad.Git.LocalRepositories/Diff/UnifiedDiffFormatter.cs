using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Pmad.Git.LocalRepositories.Diff;

/// <summary>
/// Formats differences into standard Git unified diff patch text.
/// </summary>
public static class UnifiedDiffFormatter
{
    private const int BinaryScanLimit = 8000;

    /// <summary>
    /// Determines whether the given byte buffer contains binary content (scans first 8000 bytes for NUL characters).
    /// </summary>
    /// <param name="data">The byte data to inspect.</param>
    /// <returns><see langword="true"/> if binary content was detected; otherwise, <see langword="false"/>.</returns>
    public static bool IsBinary(ReadOnlySpan<byte> data)
    {
        var scanLength = Math.Min(data.Length, BinaryScanLimit);
        for (var i = 0; i < scanLength; i++)
        {
            if (data[i] == 0)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Splits raw content into lines using UTF-8 encoding and detects whether the content ends with a trailing newline.
    /// </summary>
    /// <param name="content">The raw byte payload.</param>
    /// <returns>A tuple containing the decoded lines and trailing newline indicator.</returns>
    public static (List<string> Lines, bool HasTrailingNewline) SplitLines(byte[]? content)
    {
        if (content == null || content.Length == 0)
        {
            return (new List<string>(), true);
        }

        var hasTrailingNewline = content[^1] == (byte)'\n';
        var text = Encoding.UTF8.GetString(content);

        var lines = new List<string>();
        using var reader = new StringReader(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lines.Add(line);
        }

        return (lines, hasTrailingNewline);
    }

    /// <summary>
    /// Groups a sequence of differences into unified diff hunks with surrounding context lines.
    /// </summary>
    /// <param name="changes">The full sequence of diff changes.</param>
    /// <param name="contextLines">The number of surrounding context lines (defaults to 3).</param>
    /// <returns>A list of unified diff hunks.</returns>
    public static IReadOnlyList<UnifiedDiffHunk> CreateHunks(
        IReadOnlyList<DiffChange<string>> changes,
        int contextLines = 3)
    {
        ArgumentNullException.ThrowIfNull(changes);

        // Find all indices of actual changes (insert or delete)
        var changeIndices = new List<int>();
        for (var i = 0; i < changes.Count; i++)
        {
            if (changes[i].Type != DiffChangeType.Keep)
            {
                changeIndices.Add(i);
            }
        }

        if (changeIndices.Count == 0)
        {
            return Array.Empty<UnifiedDiffHunk>();
        }

        // Group into clusters where gap <= 2 * contextLines
        var clusters = new List<(int First, int Last)>();
        var clusterStart = changeIndices[0];
        var clusterEnd = changeIndices[0];

        for (var i = 1; i < changeIndices.Count; i++)
        {
            var idx = changeIndices[i];
            if (idx - clusterEnd <= 2 * contextLines)
            {
                clusterEnd = idx;
            }
            else
            {
                clusters.Add((clusterStart, clusterEnd));
                clusterStart = idx;
                clusterEnd = idx;
            }
        }
        clusters.Add((clusterStart, clusterEnd));

        var hunks = new List<UnifiedDiffHunk>(clusters.Count);
        foreach (var (first, last) in clusters)
        {
            var hunkStart = Math.Max(0, first - contextLines);
            var hunkEnd = Math.Min(changes.Count, last + contextLines + 1);

            var hunkChanges = new List<DiffChange<string>>(hunkEnd - hunkStart);
            for (var i = hunkStart; i < hunkEnd; i++)
            {
                hunkChanges.Add(changes[i]);
            }

            var oldLength = hunkChanges.Count(c => c.Type != DiffChangeType.Insert);
            var newLength = hunkChanges.Count(c => c.Type != DiffChangeType.Delete);

            int oldStart;
            if (oldLength > 0)
            {
                var firstOld = hunkChanges.First(c => c.Type != DiffChangeType.Insert);
                oldStart = firstOld.OldIndex + 1;
            }
            else
            {
                oldStart = 0;
            }

            int newStart;
            if (newLength > 0)
            {
                var firstNew = hunkChanges.First(c => c.Type != DiffChangeType.Delete);
                newStart = firstNew.NewIndex + 1;
            }
            else
            {
                newStart = 0;
            }

            hunks.Add(new UnifiedDiffHunk(oldStart, oldLength, newStart, newLength, hunkChanges));
        }

        return hunks;
    }

    /// <summary>
    /// Formats the unified diff for a single file.
    /// </summary>
    /// <param name="oldPath">The relative path in the original version, or <see langword="null"/> if created.</param>
    /// <param name="newPath">The relative path in the modified version, or <see langword="null"/> if deleted.</param>
    /// <param name="oldHash">The blob hash in the original version, or <see langword="null"/>.</param>
    /// <param name="newHash">The blob hash in the modified version, or <see langword="null"/>.</param>
    /// <param name="oldContent">The raw payload of the original version, or <see langword="null"/>.</param>
    /// <param name="newContent">The raw payload of the modified version, or <see langword="null"/>.</param>
    /// <param name="oldMode">The file mode in the original version (e.g. "100644").</param>
    /// <param name="newMode">The file mode in the modified version (e.g. "100644").</param>
    /// <param name="contextLines">The number of surrounding context lines (defaults to 3).</param>
    /// <returns>A tuple containing the formatted diff text, and insertion/deletion counts.</returns>
    public static (string DiffText, int Insertions, int Deletions) FormatFileDiff(
        string? oldPath,
        string? newPath,
        GitHash? oldHash,
        GitHash? newHash,
        byte[]? oldContent,
        byte[]? newContent,
        string? oldMode = null,
        string? newMode = null,
        int contextLines = 3)
    {
        if (oldHash != null && newHash != null && oldHash == newHash && (oldMode == newMode || (oldMode == null && newMode == null)))
        {
            return (string.Empty, 0, 0);
        }

        var displayPath = newPath ?? oldPath ?? string.Empty;
        var isCreated = oldContent == null && oldPath == null;
        var isDeleted = newContent == null && newPath == null;

        var oldHashStr = oldHash?.Value[..7] ?? "0000000";
        var newHashStr = newHash?.Value[..7] ?? "0000000";
        var modeStr = newMode ?? oldMode ?? "100644";
        var modeChanged = oldMode != null && newMode != null && oldMode != newMode;

        // Check if either file is binary
        var oldIsBinary = oldContent != null && IsBinary(oldContent);
        var newIsBinary = newContent != null && IsBinary(newContent);

        if (oldIsBinary || newIsBinary)
        {
            var binarySb = new StringBuilder();
            binarySb.Append("diff --git a/").Append(oldPath ?? displayPath)
                    .Append(" b/").Append(newPath ?? displayPath).Append('\n');

            if (isCreated)
            {
                binarySb.Append("new file mode ").Append(modeStr).Append('\n');
                binarySb.Append("index ").Append(oldHashStr).Append("..").Append(newHashStr).Append('\n');
                binarySb.Append("Binary files /dev/null and b/").Append(displayPath).Append(" differ\n");
            }
            else if (isDeleted)
            {
                binarySb.Append("deleted file mode ").Append(modeStr).Append('\n');
                binarySb.Append("index ").Append(oldHashStr).Append("..").Append(newHashStr).Append('\n');
                binarySb.Append("Binary files a/").Append(displayPath).Append(" and /dev/null differ\n");
            }
            else
            {
                if (modeChanged)
                {
                    binarySb.Append("old mode ").Append(oldMode).Append('\n');
                    binarySb.Append("new mode ").Append(newMode).Append('\n');
                    binarySb.Append("index ").Append(oldHashStr).Append("..").Append(newHashStr).Append('\n');
                }
                else
                {
                    binarySb.Append("index ").Append(oldHashStr).Append("..").Append(newHashStr).Append(' ').Append(modeStr).Append('\n');
                }
                binarySb.Append("Binary files a/").Append(oldPath ?? displayPath)
                        .Append(" and b/").Append(newPath ?? displayPath).Append(" differ\n");
            }

            return (binarySb.ToString(), 0, 0);
        }

        // Decode lines and trailing newline flags
        var (oldLines, oldHasNewline) = SplitLines(oldContent);
        var (newLines, newHasNewline) = SplitLines(newContent);

        var changes = MyersDiff.Compute(oldLines, newLines);
        var hunks = CreateHunks(changes, contextLines);

        if (!isCreated && !isDeleted && hunks.Count == 0)
        {
            if (modeChanged)
            {
                var modeSb = new StringBuilder();
                modeSb.Append("diff --git a/").Append(oldPath ?? displayPath)
                      .Append(" b/").Append(newPath ?? displayPath).Append('\n');
                modeSb.Append("old mode ").Append(oldMode).Append('\n');
                modeSb.Append("new mode ").Append(newMode).Append('\n');
                return (modeSb.ToString(), 0, 0);
            }

            return (string.Empty, 0, 0);
        }

        var sb = new StringBuilder();
        sb.Append("diff --git a/").Append(oldPath ?? displayPath)
          .Append(" b/").Append(newPath ?? displayPath).Append('\n');

        if (isCreated)
        {
            sb.Append("new file mode ").Append(modeStr).Append('\n');
            sb.Append("index ").Append(oldHashStr).Append("..").Append(newHashStr).Append('\n');
            if (hunks.Count > 0)
            {
                sb.Append("--- /dev/null\n");
                sb.Append("+++ b/").Append(displayPath).Append('\n');
            }
        }
        else if (isDeleted)
        {
            sb.Append("deleted file mode ").Append(modeStr).Append('\n');
            sb.Append("index ").Append(oldHashStr).Append("..").Append(newHashStr).Append('\n');
            if (hunks.Count > 0)
            {
                sb.Append("--- a/").Append(displayPath).Append('\n');
                sb.Append("+++ /dev/null\n");
            }
        }
        else
        {
            if (modeChanged)
            {
                sb.Append("old mode ").Append(oldMode).Append('\n');
                sb.Append("new mode ").Append(newMode).Append('\n');
                sb.Append("index ").Append(oldHashStr).Append("..").Append(newHashStr).Append('\n');
            }
            else
            {
                sb.Append("index ").Append(oldHashStr).Append("..").Append(newHashStr).Append(' ').Append(modeStr).Append('\n');
            }
            sb.Append("--- a/").Append(oldPath ?? displayPath).Append('\n');
            sb.Append("+++ b/").Append(newPath ?? displayPath).Append('\n');
        }

        var insertions = 0;
        var deletions = 0;

        foreach (var hunk in hunks)
        {
            sb.Append(hunk.FormatHeader()).Append('\n');

            foreach (var c in hunk.Changes)
            {
                switch (c.Type)
                {
                    case DiffChangeType.Keep:
                        sb.Append(' ').Append(c.Item).Append('\n');
                        if (c.OldIndex == oldLines.Count - 1 && !oldHasNewline &&
                            c.NewIndex == newLines.Count - 1 && !newHasNewline)
                        {
                            sb.Append("\\ No newline at end of file\n");
                        }
                        break;

                    case DiffChangeType.Delete:
                        sb.Append('-').Append(c.Item).Append('\n');
                        deletions++;
                        if (c.OldIndex == oldLines.Count - 1 && !oldHasNewline)
                        {
                            sb.Append("\\ No newline at end of file\n");
                        }
                        break;

                    case DiffChangeType.Insert:
                        sb.Append('+').Append(c.Item).Append('\n');
                        insertions++;
                        if (c.NewIndex == newLines.Count - 1 && !newHasNewline)
                        {
                            sb.Append("\\ No newline at end of file\n");
                        }
                        break;
                }
            }
        }

        return (sb.ToString(), insertions, deletions);
    }
}
