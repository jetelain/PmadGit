using System.Collections.Generic;

namespace Pmad.Git.LocalRepositories.Diff;

/// <summary>
/// Represents a unified diff hunk containing a contiguous block of changes with surrounding context lines.
/// </summary>
/// <param name="OldStart">1-based line number in the original file (or 0 if empty).</param>
/// <param name="OldLength">Number of lines from the original file in this hunk.</param>
/// <param name="NewStart">1-based line number in the modified file (or 0 if empty).</param>
/// <param name="NewLength">Number of lines from the modified file in this hunk.</param>
/// <param name="Changes">The list of change items within this hunk.</param>
public sealed record UnifiedDiffHunk(
    int OldStart,
    int OldLength,
    int NewStart,
    int NewLength,
    IReadOnlyList<DiffChange<string>> Changes)
{
    /// <summary>
    /// Formats the standard unified diff hunk header (e.g. <c>@@ -1,3 +1,4 @@</c>).
    /// </summary>
    /// <param name="sectionHeading">Optional section heading (e.g. function or class name).</param>
    /// <returns>The formatted hunk header line.</returns>
    public string FormatHeader(string? sectionHeading = null)
    {
        var oldPart = OldLength == 1 ? $"-{OldStart}" : $"-{OldStart},{OldLength}";
        var newPart = NewLength == 1 ? $"+{NewStart}" : $"+{NewStart},{NewLength}";

        if (!string.IsNullOrEmpty(sectionHeading))
        {
            return $"@@ {oldPart} {newPart} @@ {sectionHeading}";
        }

        return $"@@ {oldPart} {newPart} @@";
    }
}
