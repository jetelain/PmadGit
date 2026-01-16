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
}
