using System.Text;

namespace Pmad.Git.LocalRepositories.Diff;

/// <summary>
/// Result of a 3-way line merge.
/// </summary>
public sealed class Diff3MergeResult
{
    /// <summary>
    /// Gets whether any conflicts were encountered during the merge.
    /// </summary>
    public bool HasConflict { get; }

    /// <summary>
    /// Gets the raw UTF-8 merged content (with conflict markers if conflicts occurred).
    /// </summary>
    public byte[] MergedBytes { get; }

    /// <summary>
    /// Gets the merged text representation.
    /// </summary>
    public string MergedText { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Diff3MergeResult"/> class.
    /// </summary>
    /// <param name="hasConflict">Whether any conflicts occurred.</param>
    /// <param name="mergedBytes">Merged bytes payload.</param>
    /// <param name="mergedText">Merged text string.</param>
    public Diff3MergeResult(bool hasConflict, byte[] mergedBytes, string mergedText)
    {
        HasConflict = hasConflict;
        MergedBytes = mergedBytes;
        MergedText = mergedText;
    }
}

/// <summary>
/// Provides pure managed 3-way file and line merging capabilities based on Myers diff.
/// </summary>
public static class Diff3Merge
{
    private sealed class EditBlock
    {
        public int BaseStart { get; set; }
        public int BaseCount { get; set; }
        public int BaseEnd => BaseStart + BaseCount;
        public List<string> NewLines { get; } = new();
    }

    private sealed class Diff3Chunk
    {
        public int BaseStart { get; set; }
        public int BaseEnd { get; set; }
        public List<EditBlock> OursEdits { get; } = new();
        public List<EditBlock> TheirsEdits { get; } = new();
    }

    /// <summary>
    /// Performs a 3-way merge on raw byte contents.
    /// </summary>
    /// <param name="baseContent">Base (common ancestor) content, or null/empty if added on both branches.</param>
    /// <param name="oursContent">Ours (HEAD / local) content.</param>
    /// <param name="theirsContent">Theirs (merging / remote) content.</param>
    /// <param name="oursLabel">Label for ours conflict marker (defaults to "HEAD").</param>
    /// <param name="theirsLabel">Label for theirs conflict marker (defaults to "theirs").</param>
    /// <param name="maxEditDistance">Optional maximum edit distance threshold for Myers diff.</param>
    /// <returns>A <see cref="Diff3MergeResult"/> containing merged bytes and conflict indicator.</returns>
    public static Diff3MergeResult Merge(
        byte[]? baseContent,
        byte[]? oursContent,
        byte[]? theirsContent,
        string oursLabel = "HEAD",
        string theirsLabel = "theirs",
        int? maxEditDistance = null)
    {
        baseContent ??= Array.Empty<byte>();
        oursContent ??= Array.Empty<byte>();
        theirsContent ??= Array.Empty<byte>();

        // Fast path: if ours and theirs are identical, no merge needed
        if (oursContent.AsSpan().SequenceEqual(theirsContent))
        {
            var text = System.Text.Unicode.Utf8.IsValid(oursContent) ? Encoding.UTF8.GetString(oursContent) : string.Empty;
            return new Diff3MergeResult(false, oursContent, text);
        }

        // Fast path: if base equals theirs, take ours
        if (baseContent.AsSpan().SequenceEqual(theirsContent))
        {
            var text = System.Text.Unicode.Utf8.IsValid(oursContent) ? Encoding.UTF8.GetString(oursContent) : string.Empty;
            return new Diff3MergeResult(false, oursContent, text);
        }

        // Fast path: if base equals ours, take theirs
        if (baseContent.AsSpan().SequenceEqual(oursContent))
        {
            var text = System.Text.Unicode.Utf8.IsValid(theirsContent) ? Encoding.UTF8.GetString(theirsContent) : string.Empty;
            return new Diff3MergeResult(false, theirsContent, text);
        }

        // Strictly validate that base, ours, and theirs are valid UTF-8 and non-binary.
        // Non-UTF-8 blobs (or binary files) cannot be merged line-by-line without byte corruption.
        if (UnifiedDiffFormatter.IsBinary(oursContent) ||
            UnifiedDiffFormatter.IsBinary(theirsContent) ||
            UnifiedDiffFormatter.IsBinary(baseContent) ||
            !System.Text.Unicode.Utf8.IsValid(oursContent) ||
            !System.Text.Unicode.Utf8.IsValid(theirsContent) ||
            !System.Text.Unicode.Utf8.IsValid(baseContent))
        {
            return new Diff3MergeResult(hasConflict: true, mergedBytes: oursContent, mergedText: string.Empty);
        }

        var (baseRawLines, baseHasNewline) = UnifiedDiffFormatter.SplitLines(baseContent);
        var (oursRawLines, oursHasNewline) = UnifiedDiffFormatter.SplitLines(oursContent);
        var (theirsRawLines, theirsHasNewline) = UnifiedDiffFormatter.SplitLines(theirsContent);

        // Detect CRLF
        var hasCr = ContainsCr(oursContent) || ContainsCr(theirsContent) || ContainsCr(baseContent);
        var eol = hasCr ? "\r\n" : "\n";

        var oursDiff = MyersDiff.Compute(baseRawLines, oursRawLines, maxEditDistance: maxEditDistance);
        var theirsDiff = MyersDiff.Compute(baseRawLines, theirsRawLines, maxEditDistance: maxEditDistance);

        var oursEdits = ToEditBlocks(oursDiff);
        var theirsEdits = ToEditBlocks(theirsDiff);

        var chunks = BuildChunks(oursEdits, theirsEdits);

        var sb = new StringBuilder();
        var hasConflict = false;
        var currentBase = 0;

        foreach (var chunk in chunks)
        {
            // Append base lines before this chunk
            while (currentBase < chunk.BaseStart)
            {
                AppendLine(sb, baseRawLines[currentBase], eol);
                currentBase++;
            }

            var chunkBase = baseRawLines.Skip(chunk.BaseStart).Take(chunk.BaseEnd - chunk.BaseStart).ToList();
            var chunkOurs = ApplyEditsToSlice(baseRawLines, chunk.BaseStart, chunk.BaseEnd, chunk.OursEdits);
            var chunkTheirs = ApplyEditsToSlice(baseRawLines, chunk.BaseStart, chunk.BaseEnd, chunk.TheirsEdits);

            if (LinesEqual(chunkOurs, chunkTheirs))
            {
                // Both made identical changes
                foreach (var line in chunkOurs)
                {
                    AppendLine(sb, line, eol);
                }
            }
            else if (LinesEqual(chunkOurs, chunkBase))
            {
                // Only theirs changed
                foreach (var line in chunkTheirs)
                {
                    AppendLine(sb, line, eol);
                }
            }
            else if (LinesEqual(chunkTheirs, chunkBase))
            {
                // Only ours changed
                foreach (var line in chunkOurs)
                {
                    AppendLine(sb, line, eol);
                }
            }
            else
            {
                // Conflict
                hasConflict = true;
                sb.Append("<<<<<<< ").Append(oursLabel).Append(eol);
                foreach (var line in chunkOurs)
                {
                    AppendLine(sb, line, eol);
                }
                sb.Append("=======").Append(eol);
                foreach (var line in chunkTheirs)
                {
                    AppendLine(sb, line, eol);
                }
                sb.Append(">>>>>>> ").Append(theirsLabel).Append(eol);
            }

            currentBase = chunk.BaseEnd;
        }

        // Append remaining base lines
        while (currentBase < baseRawLines.Count)
        {
            AppendLine(sb, baseRawLines[currentBase], eol);
            currentBase++;
        }

        var mergedText = sb.ToString();

        // Adjust final newline if none of the inputs had a trailing newline and there are no conflicts
        if (!hasConflict && !oursHasNewline && !theirsHasNewline && mergedText.EndsWith(eol, StringComparison.Ordinal))
        {
            mergedText = mergedText[..^eol.Length];
        }

        var mergedBytes = Encoding.UTF8.GetBytes(mergedText);
        return new Diff3MergeResult(hasConflict, mergedBytes, mergedText);
    }

    /// <summary>
    /// Performs a 3-way merge on text strings.
    /// </summary>
    public static Diff3MergeResult Merge(
        string? baseText,
        string? oursText,
        string? theirsText,
        string oursLabel = "HEAD",
        string theirsLabel = "theirs",
        int? maxEditDistance = null)
    {
        var baseBytes = baseText != null ? Encoding.UTF8.GetBytes(baseText) : null;
        var oursBytes = oursText != null ? Encoding.UTF8.GetBytes(oursText) : null;
        var theirsBytes = theirsText != null ? Encoding.UTF8.GetBytes(theirsText) : null;

        return Merge(baseBytes, oursBytes, theirsBytes, oursLabel, theirsLabel, maxEditDistance);
    }

    private static bool ContainsCr(byte[] content)
    {
        return Array.IndexOf(content, (byte)'\r') >= 0;
    }

    private static void AppendLine(StringBuilder sb, string line, string defaultEol)
    {
        if (line.EndsWith('\r'))
        {
            sb.Append(line).Append('\n');
        }
        else
        {
            sb.Append(line).Append(defaultEol);
        }
    }

    private static bool LinesEqual(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static List<EditBlock> ToEditBlocks(IReadOnlyList<DiffChange<string>> diff)
    {
        var result = new List<EditBlock>();
        var baseIdx = 0;
        EditBlock? current = null;

        for (var i = 0; i < diff.Count; i++)
        {
            var change = diff[i];
            if (change.Type == DiffChangeType.Keep)
            {
                if (current != null)
                {
                    result.Add(current);
                    current = null;
                }
                baseIdx++;
            }
            else if (change.Type == DiffChangeType.Delete)
            {
                current ??= new EditBlock { BaseStart = baseIdx };
                current.BaseCount++;
                baseIdx++;
            }
            else // Insert
            {
                current ??= new EditBlock { BaseStart = baseIdx };
                current.NewLines.Add(change.Item);
            }
        }

        if (current != null)
        {
            result.Add(current);
        }

        return result;
    }

    private static List<Diff3Chunk> BuildChunks(
        IReadOnlyList<EditBlock> oursEdits,
        IReadOnlyList<EditBlock> theirsEdits)
    {
        var allTagged = oursEdits.Select(e => (Edit: e, IsOurs: true))
            .Concat(theirsEdits.Select(e => (Edit: e, IsOurs: false)))
            .OrderBy(x => x.Edit.BaseStart)
            .ThenBy(x => x.Edit.BaseEnd)
            .ToList();

        var chunks = new List<Diff3Chunk>();
        Diff3Chunk? current = null;

        foreach (var (edit, isOurs) in allTagged)
        {
            if (current == null)
            {
                current = new Diff3Chunk { BaseStart = edit.BaseStart, BaseEnd = edit.BaseEnd };
                AddEditToChunk(current, edit, isOurs);
                continue;
            }

            // Determine if edit overlaps or collides with current chunk
            var overlaps = edit.BaseStart < current.BaseEnd ||
                           (edit.BaseStart == current.BaseEnd && edit.BaseCount == 0 && current.BaseStart == current.BaseEnd) ||
                           (edit.BaseStart == current.BaseStart && (edit.BaseCount == 0 || current.BaseStart == current.BaseEnd));

            if (overlaps)
            {
                current.BaseEnd = Math.Max(current.BaseEnd, edit.BaseEnd);
                AddEditToChunk(current, edit, isOurs);
            }
            else
            {
                chunks.Add(current);
                current = new Diff3Chunk { BaseStart = edit.BaseStart, BaseEnd = edit.BaseEnd };
                AddEditToChunk(current, edit, isOurs);
            }
        }

        if (current != null)
        {
            chunks.Add(current);
        }

        return chunks;
    }

    private static void AddEditToChunk(Diff3Chunk chunk, EditBlock edit, bool isOurs)
    {
        if (isOurs)
        {
            chunk.OursEdits.Add(edit);
        }
        else
        {
            chunk.TheirsEdits.Add(edit);
        }
    }

    private static List<string> ApplyEditsToSlice(
        IReadOnlyList<string> baseLines,
        int baseStart,
        int baseEnd,
        IReadOnlyList<EditBlock> edits)
    {
        var result = new List<string>();
        var current = baseStart;

        foreach (var edit in edits)
        {
            while (current < edit.BaseStart)
            {
                result.Add(baseLines[current]);
                current++;
            }

            result.AddRange(edit.NewLines);
            current = edit.BaseEnd;
        }

        while (current < baseEnd)
        {
            result.Add(baseLines[current]);
            current++;
        }

        return result;
    }
}
