using System;
using System.Linq;
using System.Text;
using Pmad.Git.LocalRepositories.Diff;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public class UnifiedDiffFormatterTests
{
    [Fact]
    public void IsBinary_WithNulByte_ReturnsTrue()
    {
        var data = new byte[] { 0x48, 0x65, 0x6c, 0x6c, 0x6f, 0x00, 0x57, 0x6f, 0x72, 0x6c, 0x64 };
        Assert.True(UnifiedDiffFormatter.IsBinary(data));
    }

    [Fact]
    public void IsBinary_WithoutNulByte_ReturnsFalse()
    {
        var data = Encoding.UTF8.GetBytes("Hello, World!\nSecond line.\n");
        Assert.False(UnifiedDiffFormatter.IsBinary(data));
    }

    [Fact]
    public void SplitLines_WithTrailingNewline()
    {
        var data = Encoding.UTF8.GetBytes("line1\nline2\n");
        var (lines, hasNewline) = UnifiedDiffFormatter.SplitLines(data);

        Assert.Equal(2, lines.Count);
        Assert.Equal("line1", lines[0]);
        Assert.Equal("line2", lines[1]);
        Assert.True(hasNewline);
    }

    [Fact]
    public void SplitLines_WithoutTrailingNewline()
    {
        var data = Encoding.UTF8.GetBytes("line1\nline2");
        var (lines, hasNewline) = UnifiedDiffFormatter.SplitLines(data);

        Assert.Equal(2, lines.Count);
        Assert.Equal("line1", lines[0]);
        Assert.Equal("line2", lines[1]);
        Assert.False(hasNewline);
    }

    [Fact]
    public void FormatFileDiff_NewFile_GeneratesValidGitDiff()
    {
        var content = Encoding.UTF8.GetBytes("hello\nworld\n");
        var hash = GitHashHelper.ComputeBlobHash(content);

        var (diffText, ins, del) = UnifiedDiffFormatter.FormatFileDiff(
            oldPath: null,
            newPath: "test.txt",
            oldHash: null,
            newHash: hash,
            oldContent: null,
            newContent: content);

        Assert.Equal(2, ins);
        Assert.Equal(0, del);
        Assert.Contains("diff --git a/test.txt b/test.txt", diffText);
        Assert.Contains("new file mode 100644", diffText);
        Assert.Contains("--- /dev/null", diffText);
        Assert.Contains("+++ b/test.txt", diffText);
        Assert.Contains("@@ -0,0 +1,2 @@", diffText);
        Assert.Contains("+hello", diffText);
        Assert.Contains("+world", diffText);
    }

    [Fact]
    public void FormatFileDiff_NewFile_NoTrailingNewline_IncludesWarning()
    {
        var content = Encoding.UTF8.GetBytes("no newline");
        var hash = GitHashHelper.ComputeBlobHash(content);

        var (diffText, ins, del) = UnifiedDiffFormatter.FormatFileDiff(
            oldPath: null,
            newPath: "test.txt",
            oldHash: null,
            newHash: hash,
            oldContent: null,
            newContent: content);

        Assert.Equal(1, ins);
        Assert.Equal(0, del);
        Assert.Contains("+no newline", diffText);
        Assert.Contains("\\ No newline at end of file", diffText);
    }

    [Fact]
    public void FormatFileDiff_DeletedFile_GeneratesValidGitDiff()
    {
        var content = Encoding.UTF8.GetBytes("goodbye\n");
        var hash = GitHashHelper.ComputeBlobHash(content);

        var (diffText, ins, del) = UnifiedDiffFormatter.FormatFileDiff(
            oldPath: "test.txt",
            newPath: null,
            oldHash: hash,
            newHash: null,
            oldContent: content,
            newContent: null);

        Assert.Equal(0, ins);
        Assert.Equal(1, del);
        Assert.Contains("diff --git a/test.txt b/test.txt", diffText);
        Assert.Contains("deleted file mode 100644", diffText);
        Assert.Contains("--- a/test.txt", diffText);
        Assert.Contains("+++ /dev/null", diffText);
        Assert.Contains("@@ -1 +0,0 @@", diffText);
        Assert.Contains("-goodbye", diffText);
    }

    [Fact]
    public void FormatFileDiff_ModifiedFile_GeneratesValidHunk()
    {
        var oldContent = Encoding.UTF8.GetBytes("line1\nline2\nline3\n");
        var newContent = Encoding.UTF8.GetBytes("line1\nline2 modified\nline3\n");
        var oldHash = GitHashHelper.ComputeBlobHash(oldContent);
        var newHash = GitHashHelper.ComputeBlobHash(newContent);

        var (diffText, ins, del) = UnifiedDiffFormatter.FormatFileDiff(
            oldPath: "file.txt",
            newPath: "file.txt",
            oldHash: oldHash,
            newHash: newHash,
            oldContent: oldContent,
            newContent: newContent);

        Assert.Equal(1, ins);
        Assert.Equal(1, del);
        Assert.Contains("diff --git a/file.txt b/file.txt", diffText);
        Assert.Contains($"index {oldHash.Value[..7]}..{newHash.Value[..7]} 100644", diffText);
        Assert.Contains("--- a/file.txt", diffText);
        Assert.Contains("+++ b/file.txt", diffText);
        Assert.Contains("-line2", diffText);
        Assert.Contains("+line2 modified", diffText);
        Assert.Contains(" line1", diffText);
        Assert.Contains(" line3", diffText);
    }

    [Fact]
    public void FormatFileDiff_ModeChangeOnly_EmitsOldAndNewMode()
    {
        var content = Encoding.UTF8.GetBytes("echo hi\n");
        var hash = GitHashHelper.ComputeBlobHash(content);

        var (diffText, ins, del) = UnifiedDiffFormatter.FormatFileDiff(
            oldPath: "script.sh",
            newPath: "script.sh",
            oldHash: hash,
            newHash: hash,
            oldContent: null,
            newContent: null,
            oldMode: "100644",
            newMode: "100755");

        Assert.Equal(0, ins);
        Assert.Equal(0, del);
        Assert.Contains("diff --git a/script.sh b/script.sh", diffText);
        Assert.Contains("old mode 100644", diffText);
        Assert.Contains("new mode 100755", diffText);
        Assert.DoesNotContain("index", diffText);
    }

    [Fact]
    public void FormatFileDiff_BinaryFile_ReportsBinaryDifference()
    {
        var oldContent = new byte[] { 0x00, 0x01, 0x02 };
        var newContent = new byte[] { 0x00, 0x01, 0x03 };
        var oldHash = GitHashHelper.ComputeBlobHash(oldContent);
        var newHash = GitHashHelper.ComputeBlobHash(newContent);

        var (diffText, ins, del) = UnifiedDiffFormatter.FormatFileDiff(
            oldPath: "bin.dat",
            newPath: "bin.dat",
            oldHash: oldHash,
            newHash: newHash,
            oldContent: oldContent,
            newContent: newContent);

        Assert.Equal(0, ins);
        Assert.Equal(0, del);
        Assert.Contains("Binary files a/bin.dat and b/bin.dat differ", diffText);
    }

    [Fact]
    public void FormatFileDiff_IdenticalFiles_ReturnsEmptyString()
    {
        var content = Encoding.UTF8.GetBytes("identical content\n");
        var hash = GitHashHelper.ComputeBlobHash(content);

        var (diffText, ins, del) = UnifiedDiffFormatter.FormatFileDiff(
            oldPath: "file.txt",
            newPath: "file.txt",
            oldHash: hash,
            newHash: hash,
            oldContent: content,
            newContent: content,
            oldMode: "100644",
            newMode: "100644");

        Assert.Equal(string.Empty, diffText);
        Assert.Equal(0, ins);
        Assert.Equal(0, del);
    }
}

