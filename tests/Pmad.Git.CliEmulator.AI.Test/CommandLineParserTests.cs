using Xunit;

namespace Pmad.Git.CliEmulator.AI.Test;

public class CommandLineParserTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void ParseCommandLine_NullOrWhitespace_ReturnsEmptyArray(string input)
    {
        var result = GitAIFunctionFactory.ParseCommandLine(input);
        Assert.Empty(result);
    }

    [Fact]
    public void ParseCommandLine_SingleWord_ReturnsSingleToken()
    {
        var result = GitAIFunctionFactory.ParseCommandLine("status");
        Assert.Equal(["status"], result);
    }

    [Fact]
    public void ParseCommandLine_MultipleWords_SplitOnSpaces()
    {
        var result = GitAIFunctionFactory.ParseCommandLine("log --oneline -10");
        Assert.Equal(["log", "--oneline", "-10"], result);
    }

    [Fact]
    public void ParseCommandLine_ConsecutiveSpacesAndTabs_HandledCorrectly()
    {
        var result = GitAIFunctionFactory.ParseCommandLine("commit   -m   \t  message");
        Assert.Equal(["commit", "-m", "message"], result);
    }

    [Theory]
    [InlineData("commit -m 'Initial commit'", new[] { "commit", "-m", "Initial commit" })]
    [InlineData("commit -m \"Initial commit\"", new[] { "commit", "-m", "Initial commit" })]
    [InlineData("add 'path with spaces/file.txt'", new[] { "add", "path with spaces/file.txt" })]
    [InlineData("add \"path with spaces/file.txt\"", new[] { "add", "path with spaces/file.txt" })]
    public void ParseCommandLine_QuotedArguments_PreserveSpaces(string input, string[] expected)
    {
        var result = GitAIFunctionFactory.ParseCommandLine(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseCommandLine_EmptyDoubleQuotes_PreservesEmptyStringToken()
    {
        var result = GitAIFunctionFactory.ParseCommandLine("commit -m \"\"");
        Assert.Equal(["commit", "-m", ""], result);
    }

    [Fact]
    public void ParseCommandLine_EmptySingleQuotes_PreservesEmptyStringToken()
    {
        var result = GitAIFunctionFactory.ParseCommandLine("commit -m ''");
        Assert.Equal(["commit", "-m", ""], result);
    }

    [Fact]
    public void ParseCommandLine_NestedQuotes_PreservesInnerQuotes()
    {
        var resultSingleInDouble = GitAIFunctionFactory.ParseCommandLine("commit -m \"Nested 'single' quotes\"");
        Assert.Equal(["commit", "-m", "Nested 'single' quotes"], resultSingleInDouble);

        var resultDoubleInSingle = GitAIFunctionFactory.ParseCommandLine("commit -m 'Nested \"double\" quotes'");
        Assert.Equal(["commit", "-m", "Nested \"double\" quotes"], resultDoubleInSingle);
    }

    [Fact]
    public void ParseCommandLine_EscapedQuotes_UnescapedProperly()
    {
        var result = GitAIFunctionFactory.ParseCommandLine("commit -m \"hello \\\"world\\\"\"");
        Assert.Equal(["commit", "-m", "hello \"world\""], result);

        var resultSingle = GitAIFunctionFactory.ParseCommandLine("commit -m 'hello \\'world\\''");
        Assert.Equal(["commit", "-m", "hello 'world'"], resultSingle);
    }

    [Fact]
    public void ParseCommandLine_EscapedBackslash_UnescapedProperly()
    {
        var result = GitAIFunctionFactory.ParseCommandLine("commit -m \"path\\\\to\\\\file\"");
        Assert.Equal(["commit", "-m", "path\\to\\file"], result);
    }

    [Fact]
    public void ParseCommandLine_ComplexCommandLine_ParsesCorrectly()
    {
        var result = GitAIFunctionFactory.ParseCommandLine("  branch -m   'old branch'   \"new branch\"  ");
        Assert.Equal(["branch", "-m", "old branch", "new branch"], result);
    }
}
