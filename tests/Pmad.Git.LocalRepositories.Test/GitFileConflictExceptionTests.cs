using System;
using Pmad.Git.LocalRepositories;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitFileConflictExceptionTests
{
    [Fact]
    public void DefaultConstructor_CreatesInstance()
    {
        var ex = new GitFileConflictException();

        Assert.NotNull(ex);
        Assert.Null(ex.FilePath);
    }

    [Fact]
    public void MessageConstructor_SetsMessage()
    {
        const string message = "A conflict occurred.";

        var ex = new GitFileConflictException(message);

        Assert.Equal(message, ex.Message);
        Assert.Null(ex.FilePath);
    }

    [Fact]
    public void MessageAndFilePathConstructor_SetsMessageAndFilePath()
    {
        const string message = "A conflict occurred.";
        const string filePath = "src/file.txt";

        var ex = new GitFileConflictException(message, filePath);

        Assert.Equal(message, ex.Message);
        Assert.Equal(filePath, ex.FilePath);
    }

    [Fact]
    public void MessageAndFilePathConstructor_NullFilePath_SetsFilePathToNull()
    {
        var ex = new GitFileConflictException("msg", (string?)null);

        Assert.Null(ex.FilePath);
    }

    [Fact]
    public void MessageAndInnerExceptionConstructor_SetsMessageAndInnerException()
    {
        const string message = "Outer message.";
        var inner = new InvalidOperationException("Inner message.");

        var ex = new GitFileConflictException(message, inner);

        Assert.Equal(message, ex.Message);
        Assert.Same(inner, ex.InnerException);
        Assert.Null(ex.FilePath);
    }

    [Fact]
    public void IsInvalidOperationException()
    {
        var ex = new GitFileConflictException();

        Assert.IsAssignableFrom<InvalidOperationException>(ex);
    }

    [Fact]
    public void CanBeCaughtAsInvalidOperationException()
    {
        const string filePath = "docs/readme.md";

        InvalidOperationException? caught = null;
        try
        {
            throw new GitFileConflictException("conflict", filePath);
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        Assert.NotNull(caught);
        Assert.IsType<GitFileConflictException>(caught);
        Assert.Equal(filePath, ((GitFileConflictException)caught).FilePath);
    }
}
