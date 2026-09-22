using Pmad.Git.LocalRepositories.Diff;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class Diff3MergeTests
{
    [Fact]
    public void Merge_OursAndTheirsIdentical_ReturnsClean()
    {
        var baseText = "line1\nline2\nline3\n";
        var oursText = "line1\nline2_mod\nline3\n";
        var theirsText = "line1\nline2_mod\nline3\n";

        var result = Diff3Merge.Merge(baseText, oursText, theirsText);

        Assert.False(result.HasConflict);
        Assert.Equal(oursText, result.MergedText);
    }

    [Fact]
    public void Merge_OnlyOursModified_ReturnsOurs()
    {
        var baseText = "line1\nline2\nline3\n";
        var oursText = "line1\nline2_ours\nline3\n";
        var theirsText = "line1\nline2\nline3\n";

        var result = Diff3Merge.Merge(baseText, oursText, theirsText);

        Assert.False(result.HasConflict);
        Assert.Equal(oursText, result.MergedText);
    }

    [Fact]
    public void Merge_OnlyTheirsModified_ReturnsTheirs()
    {
        var baseText = "line1\nline2\nline3\n";
        var oursText = "line1\nline2\nline3\n";
        var theirsText = "line1\nline2_theirs\nline3\n";

        var result = Diff3Merge.Merge(baseText, oursText, theirsText);

        Assert.False(result.HasConflict);
        Assert.Equal(theirsText, result.MergedText);
    }

    [Fact]
    public void Merge_DisjointEditsInSameFile_MergesCleanly()
    {
        var baseText = "line1\nline2\nline3\nline4\nline5\n";
        var oursText = "line1_modified\nline2\nline3\nline4\nline5\n";
        var theirsText = "line1\nline2\nline3\nline4\nline5_modified\n";

        var result = Diff3Merge.Merge(baseText, oursText, theirsText);

        Assert.False(result.HasConflict);
        var expected = "line1_modified\nline2\nline3\nline4\nline5_modified\n";
        Assert.Equal(expected, result.MergedText);
    }

    [Fact]
    public void Merge_ConflictingEditsInSameLine_EmitsConflictMarkers()
    {
        var baseText = "line1\nline2\nline3\n";
        var oursText = "line1\nline2_ours\nline3\n";
        var theirsText = "line1\nline2_theirs\nline3\n";

        var result = Diff3Merge.Merge(baseText, oursText, theirsText, oursLabel: "HEAD", theirsLabel: "feature");

        Assert.True(result.HasConflict);
        var expected = "line1\n<<<<<<< HEAD\nline2_ours\n=======\nline2_theirs\n>>>>>>> feature\nline3\n";
        Assert.Equal(expected, result.MergedText);
    }

    [Fact]
    public void Merge_IdenticalEditsInSameLine_MergesCleanly()
    {
        var baseText = "line1\nline2\nline3\n";
        var oursText = "line1\nline2_same\nline3\n";
        var theirsText = "line1\nline2_same\nline3\n";

        var result = Diff3Merge.Merge(baseText, oursText, theirsText);

        Assert.False(result.HasConflict);
        Assert.Equal(oursText, result.MergedText);
    }

    [Fact]
    public void Merge_BothAddDifferentLinesToEmptyBase_EmitsConflict()
    {
        var baseText = "";
        var oursText = "from ours\n";
        var theirsText = "from theirs\n";

        var result = Diff3Merge.Merge(baseText, oursText, theirsText, "HEAD", "feature");

        Assert.True(result.HasConflict);
        var expected = "<<<<<<< HEAD\nfrom ours\n=======\nfrom theirs\n>>>>>>> feature\n";
        Assert.Equal(expected, result.MergedText);
    }

    [Fact]
    public void Merge_BothAddSameLinesToEmptyBase_MergesCleanly()
    {
        var baseText = "";
        var oursText = "same content\n";
        var theirsText = "same content\n";

        var result = Diff3Merge.Merge(baseText, oursText, theirsText);

        Assert.False(result.HasConflict);
        Assert.Equal("same content\n", result.MergedText);
    }

    [Fact]
    public void Merge_PreservesCrlfLineEndings()
    {
        var baseText = "line1\r\nline2\r\nline3\r\n";
        var oursText = "line1\r\nline2_ours\r\nline3\r\n";
        var theirsText = "line1\r\nline2_theirs\r\nline3\r\n";

        var result = Diff3Merge.Merge(baseText, oursText, theirsText, "HEAD", "feature");

        Assert.True(result.HasConflict);
        var expected = "line1\r\n<<<<<<< HEAD\r\nline2_ours\r\n=======\r\nline2_theirs\r\n>>>>>>> feature\r\nline3\r\n";
        Assert.Equal(expected, result.MergedText);
    }
}

