using Pmad.Git.CliEmulator;
using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Internal;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class ApprovalHelperTests
{
    [Theory]
    [InlineData("https://user:password@github.com/org/repo.git", "https://github.com/org/repo.git")]
    [InlineData("https://token@github.com/org/repo.git", "https://github.com/org/repo.git")]
    [InlineData("http://foo:bar@localhost:8080/test.git", "http://localhost:8080/test.git")]
    [InlineData("https://github.com/org/repo.git", "https://github.com/org/repo.git")]
    [InlineData("git@github.com:org/repo.git", "git@github.com:org/repo.git")]
    [InlineData("origin", "origin")]
    [InlineData("/var/git/repo.git", "/var/git/repo.git")]
    [InlineData("", "")]
    public void SanitizeUrl_RemovesCredentialsFromAbsoluteUri(string input, string expected)
    {
        var sanitized = ApprovalHelper.SanitizeUrl(input);
        Assert.Equal(expected, sanitized);
    }

    [Fact]
    public async Task RequireAsync_WhenApproved_DoesNotThrow()
    {
        var context = new DiscardChangesContext { Operation = "test", AffectedFiles = [] };
        await ApprovalHelper.RequireAsync(context, (c, ct) => Task.FromResult(ApprovalResult.Approved), CancellationToken.None);
    }

    [Fact]
    public async Task RequireAsync_WhenDenied_ThrowsGitCliDeniedException()
    {
        var context = new DiscardChangesContext { Operation = "my-operation", AffectedFiles = [] };
        var ex = await Assert.ThrowsAsync<GitCliDeniedException>(() =>
            ApprovalHelper.RequireAsync(context, (c, ct) => Task.FromResult(ApprovalResult.Denied), CancellationToken.None));
        Assert.Equal("my-operation", ex.Operation);
        Assert.Contains("my-operation", ex.Message);
    }

    [Fact]
    public async Task RequireAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var context = new DiscardChangesContext { Operation = "my-operation", AffectedFiles = [] };
        var ex = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ApprovalHelper.RequireAsync(context, (c, ct) => Task.FromResult(ApprovalResult.Cancelled), CancellationToken.None));
        Assert.Contains("my-operation", ex.Message);
    }

    [Fact]
    public async Task ToSummary_CreatesValidSummary()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var commit = await repo.GetCommitAsync(testRepo.Head.ToString());

        var summary = ApprovalHelper.ToSummary(commit);

        Assert.Equal(testRepo.Head.ToString(), summary.Hash);
        Assert.Equal(testRepo.Head.ToString()[..7], summary.ShortHash);
        Assert.Equal("Initial commit", summary.Subject);
        Assert.Equal("Test User", summary.Author);
        Assert.Equal("test@example.com", summary.AuthorEmail);
    }
}
