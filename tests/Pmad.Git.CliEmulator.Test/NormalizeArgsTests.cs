using Xunit;

namespace Pmad.Git.CliEmulator.Test;

public class NormalizeArgsTests
{
    [Fact]
    public void NormalizeArgs_EmptyArray_ReturnsEmptyArray()
    {
        var result = GitCliEmulator.NormalizeArgs([]);
        Assert.Empty(result);
    }

    [Theory]
    [InlineData("status")]
    [InlineData("diff")]
    [InlineData("branch")]
    [InlineData("fetch")]
    public void NormalizeArgs_NonLogCommands_ReturnsUnmodified(string command)
    {
        string[] args = [command];
        var result = GitCliEmulator.NormalizeArgs(args);
        Assert.Equal(args, result);
    }

    [Fact]
    public void NormalizeArgs_NonLogCommand_WithNegativeNumber_DoesNotModify()
    {
        // For example, commit message starting with a negative number
        string[] args = ["commit", "-m", "-10"];
        var result = GitCliEmulator.NormalizeArgs(args);
        Assert.Equal(args, result);
    }

    [Fact]
    public void NormalizeArgs_Log_WithoutNegativeNumber_ReturnsUnmodified()
    {
        string[] args = ["log", "--oneline", "-n", "10"];
        var result = GitCliEmulator.NormalizeArgs(args);
        Assert.Equal(args, result);
    }

    [Theory]
    [InlineData("-1", "1")]
    [InlineData("-5", "5")]
    [InlineData("-10", "10")]
    [InlineData("-100", "100")]
    [InlineData("-0", "0")]
    public void NormalizeArgs_Log_NegativeNumberShorthand_NormalizedToNFlag(string flag, string expectedCount)
    {
        string[] args = ["log", flag];
        var result = GitCliEmulator.NormalizeArgs(args);
        Assert.Equal(["log", "-n", expectedCount], result);
    }

    [Fact]
    public void NormalizeArgs_Log_NegativeNumberBetweenOtherFlags_NormalizedInPlace()
    {
        string[] args = ["log", "--oneline", "-15", "--graph"];
        var result = GitCliEmulator.NormalizeArgs(args);
        Assert.Equal(["log", "--oneline", "-n", "15", "--graph"], result);
    }

    [Theory]
    [InlineData("-")]
    [InlineData("-p")]
    [InlineData("--graph")]
    [InlineData("-abc")]
    [InlineData("-10a")]
    [InlineData("-a10")]
    public void NormalizeArgs_Log_NonDigitFlags_NotModified(string flag)
    {
        string[] args = ["log", flag];
        var result = GitCliEmulator.NormalizeArgs(args);
        Assert.Equal(args, result);
    }

    [Fact]
    public void NormalizeArgs_GlobalOptionsBeforeLog_StillNormalizesLogArguments()
    {
        string[] args = ["-C", "some/path", "log", "-10"];
        var result = GitCliEmulator.NormalizeArgs(args);
        Assert.Equal(["-C", "some/path", "log", "-n", "10"], result);
    }
}
