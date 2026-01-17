using System;
using System.Text;
using Pmad.Git.LocalRepositories;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitCommitTests
{
    [Fact]
    public void Parse_ReadsTreeParentsHeadersAndMessage()
    {
        var commitId = new GitHash("0123456789abcdef0123456789abcdef01234567");
        var treeId = new GitHash("89abcdef0123456789abcdef0123456789abcdef");
        var parentOne = new GitHash("fedcba9876543210fedcba9876543210fedcba98");
        var parentTwo = new GitHash("00112233445566778899aabbccddeeff00112233");

        var payload = $"tree {treeId}\n" +
                      $"parent {parentOne}\n" +
                      $"parent {parentTwo}\n" +
                      "author John Doe <john@example.com>\n" +
                      "committer Jane Doe <jane@example.com>\n\n" +
                      "Commit message\nSecond line";

        var commit = GitCommit.Parse(commitId, Encoding.UTF8.GetBytes(payload));

        Assert.Equal(treeId, commit.Tree);
        Assert.Equal("Commit message\nSecond line", commit.Message);
        Assert.Equal("John Doe <john@example.com>", commit.Author);
        Assert.Equal("Jane Doe <jane@example.com>", commit.Committer);
        Assert.Collection(
            commit.Parents,
            first => Assert.Equal(parentOne, first),
            second => Assert.Equal(parentTwo, second));
    }

    [Fact]
    public void Parse_WithoutMessage_ProducesEmptyString()
    {
        var commitId = new GitHash("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var treeId = new GitHash("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        var payload = $"tree {treeId}\n" +
                      "author Someone <user@example.com>\n" +
                      "committer Someone <user@example.com>\n";

        var commit = GitCommit.Parse(commitId, Encoding.UTF8.GetBytes(payload));

        Assert.Equal(string.Empty, commit.Message);
    }

    [Fact]
    public void Parse_MissingTree_Throws()
    {
        var commitId = new GitHash("cccccccccccccccccccccccccccccccccccccccc");
        var payload = "parent aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\n\nmessage";

        Assert.Throws<InvalidOperationException>(() => GitCommit.Parse(commitId, Encoding.UTF8.GetBytes(payload)));
    }

	[Fact]
	public void ToMetadata_ParsesAuthorAndCommitterSignatures()
	{
		var commitId = new GitHash("dddddddddddddddddddddddddddddddddddddddd");
		var treeId = new GitHash("eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee");
		var payload = $"tree {treeId}\n" +
		             "author Alice Author <alice@example.com> 1700000000 +0230\n" +
		             "committer Carol Committer <carol@example.com> 1700003600 -0100\n\n" +
		             "Metadata message";

		var commit = GitCommit.Parse(commitId, Encoding.UTF8.GetBytes(payload));
		var metadata = commit.Metadata;

		Assert.Equal("Metadata message", metadata.Message);
		Assert.Equal("Alice Author", metadata.AuthorName);
		Assert.Equal("alice@example.com", metadata.AuthorEmail);
		Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).ToOffset(new TimeSpan(2, 30, 0)), metadata.AuthorDate);
		Assert.Equal("Carol Committer", metadata.CommitterName);
		Assert.Equal("carol@example.com", metadata.CommitterEmail);
		Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_003_600).ToOffset(new TimeSpan(-1, 0, 0)), metadata.CommitterDate);
	}

	[Fact]
	public void ToMetadata_FallsBackToAuthorWhenCommitterMissing()
	{
		var commitId = new GitHash("ffffffffffffffffffffffffffffffffffffffff");
		var treeId = new GitHash("1111111111111111111111111111111111111111");
		var payload = $"tree {treeId}\n" +
		             "author Solo Author <solo@example.com> 1700000000 +0000\n\n" +
		             "Solo message";

		var commit = GitCommit.Parse(commitId, Encoding.UTF8.GetBytes(payload));
		var metadata = commit.Metadata;

		Assert.Equal("Solo Author", metadata.CommitterName);
		Assert.Equal("solo@example.com", metadata.CommitterEmail);
		Assert.Equal(metadata.AuthorDate, metadata.CommitterDate);
	}
}
