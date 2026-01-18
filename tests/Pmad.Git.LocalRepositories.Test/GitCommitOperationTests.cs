using System;
using Pmad.Git.LocalRepositories;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitCommitOperationTests
{
    private sealed class TestOperation : GitCommitOperation
    {
        public TestOperation(string path) : base(path)
        {
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidPath_Throws(string? path)
    {
        Assert.Throws<ArgumentException>(() => new TestOperation(path!));
    }

    [Fact]
    public void Constructor_SetsPathProperty()
    {
        const string expected = "src/file.txt";
        var operation = new TestOperation(expected);

        Assert.Equal(expected, operation.Path);
    }
}
