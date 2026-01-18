using System;
using Pmad.Git.LocalRepositories;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitCommitMetadataTests
{
    private static GitCommitSignature CreateSignature(string name = "Author")
        => new(name, $"{name.ToLowerInvariant()}@example.com", DateTimeOffset.UnixEpoch);

    [Fact]
    public void Constructor_NullAuthor_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new GitCommitMetadata("message", null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Constructor_InvalidMessage_Throws(string? message)
    {
        var author = CreateSignature();
        Assert.Throws<ArgumentException>(() => new GitCommitMetadata(message!, author));
    }

    [Fact]
    public void Constructor_DefaultsCommitterToAuthorWhenMissing()
    {
        var author = CreateSignature();
        var metadata = new GitCommitMetadata("message", author);

        Assert.Same(author, metadata.Author);
        Assert.Same(author, metadata.Committer);
    }

    [Fact]
    public void Constructor_PreservesExplicitCommitter()
    {
        var author = CreateSignature("Author");
        var committer = CreateSignature("Committer");
        var metadata = new GitCommitMetadata("message", author, committer);

        Assert.Same(author, metadata.Author);
        Assert.Same(committer, metadata.Committer);
    }

    [Fact]
    public void Properties_ExposeSignatureDetails()
    {
        var author = new GitCommitSignature("Alice", "alice@example.com", DateTimeOffset.FromUnixTimeSeconds(1));
        var committer = new GitCommitSignature("Bob", "bob@example.com", DateTimeOffset.FromUnixTimeSeconds(2));
        var metadata = new GitCommitMetadata("message", author, committer);

        Assert.Equal("Alice", metadata.AuthorName);
        Assert.Equal("alice@example.com", metadata.AuthorEmail);
        Assert.Equal(author.Timestamp, metadata.AuthorDate);
        Assert.Equal("Bob", metadata.CommitterName);
        Assert.Equal("bob@example.com", metadata.CommitterEmail);
        Assert.Equal(committer.Timestamp, metadata.CommitterDate);
    }
}
