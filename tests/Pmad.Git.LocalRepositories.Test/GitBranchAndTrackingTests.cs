using System;
using System.IO;
using System.Threading.Tasks;
using Pmad.Git.Tests.Infrastructure;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitBranchAndTrackingTests
{
    [Fact]
    public async Task GetBranchesAsync_ReturnsLocalAndRemoteBranches()
    {
        using var repo = GitTestRepository.Create();
        repo.RunGit("branch feature-1");
        repo.RunGit("branch feature-2");

        var git = GitRepository.Open(repo.WorkingDirectory);
        var branches = await git.GetBranchesAsync();

        Assert.Contains("master", branches);
        Assert.Contains("feature-1", branches);
        Assert.Contains("feature-2", branches);
    }

    [Fact]
    public async Task CreateBranchAsync_CreatesNewBranchPointingToCommit()
    {
        using var repo = GitTestRepository.Create();
        var git = GitRepository.Open(repo.WorkingDirectory);

        await git.CreateBranchAsync("dev");
        var branches = await git.GetBranchesAsync();

        Assert.Contains("dev", branches);
        var devCommit = await git.GetCommitAsync("dev");
        var headCommit = await git.GetCommitAsync();
        Assert.Equal(headCommit.Id, devCommit.Id);
    }

    [Fact]
    public async Task RenameBranchAsync_RenamesBranchAndUpdatesHead()
    {
        using var repo = GitTestRepository.Create();
        var git = GitRepository.Open(repo.WorkingDirectory);

        // Currently on master
        await git.RenameBranchAsync("master", "main");

        var currentBranch = await git.GetCurrentBranchNameAsync();
        Assert.Equal("main", currentBranch);

        var branches = await git.GetBranchesAsync();
        Assert.Contains("main", branches);
        Assert.DoesNotContain("master", branches);
    }

    [Fact]
    public async Task DeleteBranchAsync_CannotDeleteCurrentBranch()
    {
        using var repo = GitTestRepository.Create();
        var git = GitRepository.Open(repo.WorkingDirectory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => git.DeleteBranchAsync("master"));
    }

    [Fact]
    public async Task DeleteBranchAsync_EnforcesUnmergedCheckUnlessForced()
    {
        using var repo = GitTestRepository.Create();
        var git = GitRepository.Open(repo.WorkingDirectory);

        await git.CreateBranchAsync("unmerged");
        // Add a commit to unmerged branch
        repo.RunGit("checkout unmerged");
        repo.Commit("Unmerged change", ("unmerged.txt", "content"));
        repo.RunGit("checkout master");

        // Safe delete should fail because unmerged
        await Assert.ThrowsAsync<InvalidOperationException>(() => git.DeleteBranchAsync("unmerged", force: false));

        // Forced delete succeeds
        await git.DeleteBranchAsync("unmerged", force: true);
        var branches = await git.GetBranchesAsync();
        Assert.DoesNotContain("unmerged", branches);
    }

    [Fact]
    public async Task DeleteBranchAsync_SucceedsWhenMerged()
    {
        using var repo = GitTestRepository.Create();
        var git = GitRepository.Open(repo.WorkingDirectory);

        await git.CreateBranchAsync("merged-branch");
        await git.DeleteBranchAsync("merged-branch", force: false);

        var branches = await git.GetBranchesAsync();
        Assert.DoesNotContain("merged-branch", branches);
    }

    [Fact]
    public async Task GetTrackingStatusAsync_WithoutUpstream_ReturnsNoUpstream()
    {
        using var repo = GitTestRepository.Create();
        var git = GitRepository.Open(repo.WorkingDirectory);

        var status = await git.GetTrackingStatusAsync();

        Assert.Equal("master", status.LocalBranch);
        Assert.Null(status.UpstreamBranch);
        Assert.False(status.HasUpstream);
        Assert.False(status.IsSynchronized);
        Assert.Equal(0, status.AheadCount);
        Assert.Equal(0, status.BehindCount);
    }

    [Fact]
    public async Task GetTrackingStatusAsync_SynchronizedAndAheadBehind_CalculatesCorrectly()
    {
        using var repo = GitTestRepository.Create();
        var git = GitRepository.Open(repo.WorkingDirectory);

        var baseCommit = repo.Head;

        // Configure upstream in .git/config
        await git.SetConfigAsync("branch.master.remote", "origin");
        await git.SetConfigAsync("branch.master.merge", "refs/heads/master");

        // Set remote ref refs/remotes/origin/master to baseCommit
        await git.ReferenceStore.CreateReferenceAsync("refs/remotes/origin/master", baseCommit, overwrite: true);

        // 1. Initially synchronized
        var status = await git.GetTrackingStatusAsync();
        Assert.True(status.HasUpstream);
        Assert.Equal("origin/master", status.UpstreamBranch);
        Assert.True(status.IsSynchronized);
        Assert.Equal(0, status.AheadCount);
        Assert.Equal(0, status.BehindCount);

        // 2. Local makes 2 commits ahead
        var ahead1 = repo.Commit("Ahead 1", ("file1.txt", "1"));
        var ahead2 = repo.Commit("Ahead 2", ("file2.txt", "2"));
        git.InvalidateCaches();

        status = await git.GetTrackingStatusAsync();
        Assert.True(status.HasUnpushedCommits);
        Assert.False(status.HasUnpulledCommits);
        Assert.False(status.IsSynchronized);
        Assert.Equal(2, status.AheadCount);
        Assert.Equal(0, status.BehindCount);

        // 3. Remote gets an alternate commit (diverged: 2 ahead, 1 behind)
        repo.RunGit("checkout -b remote-work " + baseCommit.Value);
        var behindCommit = repo.Commit("Remote commit", ("remote.txt", "from remote"));
        repo.RunGit("checkout master");
        git.InvalidateCaches();

        await git.ReferenceStore.CreateReferenceAsync("refs/remotes/origin/master", behindCommit, overwrite: true);

        status = await git.GetTrackingStatusAsync();
        Assert.True(status.HasUnpushedCommits);
        Assert.True(status.HasUnpulledCommits);
        Assert.False(status.IsSynchronized);
        Assert.Equal(2, status.AheadCount);
        Assert.Equal(1, status.BehindCount);
    }

    [Fact]
    public async Task IsCommitPushedAsync_ReturnsTrueWhenPushed_FalseWhenNotPushed()
    {
        using var repo = GitTestRepository.Create();
        var git = GitRepository.Open(repo.WorkingDirectory);

        var pushedCommit = repo.Head;
        await git.ReferenceStore.CreateReferenceAsync("refs/remotes/origin/master", pushedCommit, overwrite: true);

        var unpushedCommit = repo.Commit("Local only", ("local.txt", "secret"));
        git.InvalidateCaches();

        Assert.True(await git.IsCommitPushedAsync(pushedCommit));
        Assert.True(await git.IsCommitPushedAsync(pushedCommit, "origin/master"));

        Assert.False(await git.IsCommitPushedAsync(unpushedCommit));
        Assert.False(await git.IsCommitPushedAsync(unpushedCommit, "origin/master"));
    }

    [Fact]
    public async Task ConfigOperations_GetSetUnset_WorkAsExpected()
    {
        using var repo = GitTestRepository.Create();
        var git = GitRepository.Open(repo.WorkingDirectory);

        await git.SetConfigAsync("test.section.key", "hello-world");
        var value = await git.GetConfigAsync("test.section.key");
        Assert.Equal("hello-world", value);

        await git.UnsetConfigAsync("test.section.key");
        var unsetValue = await git.GetConfigAsync("test.section.key");
        Assert.Null(unsetValue);
    }
}
