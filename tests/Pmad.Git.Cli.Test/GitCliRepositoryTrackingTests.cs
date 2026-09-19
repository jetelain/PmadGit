using Pmad.Git.Cli.Test.Infrastructure;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.Cli.Test;

public class GitCliRepositoryTrackingTests
{
    [Fact]
    public async Task GetTrackingStatusAsync_WithoutUpstream_ReturnsNoUpstream()
    {
        using var repo = GitCliTestRepository.Create();
        var git = new GitCliRepository(repo.WorkingDirectory);

        var status = await git.GetTrackingStatusAsync();

        Assert.Equal(GitCliTestRepository.DefaultBranch, status.LocalBranch);
        Assert.Null(status.UpstreamBranch);
        Assert.False(status.HasUpstream);
        Assert.False(status.HasUnpushedCommits);
        Assert.False(status.HasUnpulledCommits);
        Assert.False(status.IsSynchronized);
        Assert.Equal(0, status.AheadCount);
        Assert.Equal(0, status.BehindCount);
    }

    [Fact]
    public async Task GetTrackingStatusAsync_Synchronized_ReturnsIsSynchronized()
    {
        using var remote = GitCliTestRepository.CreateBare();
        using var repo = GitCliTestRepository.Create();
        repo.AddRemote("origin", remote);

        var git = new GitCliRepository(repo.WorkingDirectory);
        await git.PushAsync("origin", GitCliTestRepository.DefaultBranch, setUpstream: true);

        var status = await git.GetTrackingStatusAsync();

        Assert.Equal(GitCliTestRepository.DefaultBranch, status.LocalBranch);
        Assert.Equal($"origin/{GitCliTestRepository.DefaultBranch}", status.UpstreamBranch);
        Assert.True(status.HasUpstream);
        Assert.True(status.IsSynchronized);
        Assert.False(status.HasUnpushedCommits);
        Assert.False(status.HasUnpulledCommits);
        Assert.Equal(0, status.AheadCount);
        Assert.Equal(0, status.BehindCount);
    }

    [Fact]
    public async Task GetTrackingStatusAsync_AheadAndBehind_CountsCorrectly()
    {
        using var remote = GitCliTestRepository.CreateBare();
        using var repoA = GitCliTestRepository.Create();
        repoA.AddRemote("origin", remote);

        var gitA = new GitCliRepository(repoA.WorkingDirectory);
        await gitA.PushAsync("origin", GitCliTestRepository.DefaultBranch, setUpstream: true);

        using var repoB = GitCliTestRepository.Clone(remote);
        repoB.Commit("Commit from B", ("fileB.txt", "contentB"));
        var gitB = new GitCliRepository(repoB.WorkingDirectory);
        await gitB.PushAsync();

        // Local A creates 2 commits ahead
        repoA.Commit("Commit 1 from A", ("fileA1.txt", "contentA1"));
        repoA.Commit("Commit 2 from A", ("fileA2.txt", "contentA2"));

        // Fetch remote in A without merging
        await gitA.FetchAsync();

        var status = await gitA.GetTrackingStatusAsync();

        Assert.True(status.HasUpstream);
        Assert.False(status.IsSynchronized);
        Assert.True(status.HasUnpushedCommits);
        Assert.True(status.HasUnpulledCommits);
        Assert.Equal(2, status.AheadCount);
        Assert.Equal(1, status.BehindCount);
    }

    [Fact]
    public async Task IsCommitPushedAsync_ReturnsTrue_WhenPushed_AndFalse_WhenUnpushed()
    {
        using var remote = GitCliTestRepository.CreateBare();
        using var repo = GitCliTestRepository.Create();
        repo.AddRemote("origin", remote);

        var git = new GitCliRepository(repo.WorkingDirectory);
        await git.PushAsync("origin", GitCliTestRepository.DefaultBranch, setUpstream: true);

        var initialCommitHash = repo.RunGit("rev-parse HEAD").Trim();
        Assert.True(await git.IsCommitPushedAsync(initialCommitHash));
        Assert.True(await git.IsCommitPushedAsync(new GitHash(initialCommitHash), $"origin/{GitCliTestRepository.DefaultBranch}"));

        repo.Commit("Unpushed local commit", ("unpushed.txt", "content"));
        var unpushedCommitHash = repo.RunGit("rev-parse HEAD").Trim();

        Assert.False(await git.IsCommitPushedAsync(unpushedCommitHash));
        Assert.False(await git.IsCommitPushedAsync(new GitHash(unpushedCommitHash), $"origin/{GitCliTestRepository.DefaultBranch}"));

        await git.PushAsync();

        Assert.True(await git.IsCommitPushedAsync(unpushedCommitHash));
        Assert.True(await git.IsCommitPushedAsync(new GitHash(unpushedCommitHash), $"origin/{GitCliTestRepository.DefaultBranch}"));
    }

    [Fact]
    public async Task ConfigAsync_Set_Get_And_Unset_WorkAsExpected()
    {
        using var repo = GitCliTestRepository.Create();
        var git = new GitCliRepository(repo.WorkingDirectory);

        var unset = await git.GetConfigAsync("custom.testkey");
        Assert.Null(unset);

        await git.SetConfigAsync("custom.testkey", "testvalue");
        var val = await git.GetConfigAsync("custom.testkey");
        Assert.Equal("testvalue", val);

        await git.UnsetConfigAsync("custom.testkey");
        var removed = await git.GetConfigAsync("custom.testkey");
        Assert.Null(removed);
    }

    [Fact]
    public async Task CommitAsync_And_CommitAmendAsync_WorkAsExpected()
    {
        using var repo = GitCliTestRepository.Create();
        var git = new GitCliRepository(repo.WorkingDirectory);

        repo.WriteFile("doc.txt", "version 1");
        await git.RunAsync("add", "doc.txt");
        await git.CommitAsync("Added doc version 1");

        var log1 = repo.RunGit("log -1 --pretty=%B").Trim();
        Assert.Equal("Added doc version 1", log1);

        repo.WriteFile("doc.txt", "version 2");
        await git.CommitAmendAsync("Amended doc version 2", all: true);

        var log2 = repo.RunGit("log -1 --pretty=%B").Trim();
        Assert.Equal("Amended doc version 2", log2);

        // CommitAmendAsync with no message (retains previous)
        repo.WriteFile("doc.txt", "version 3");
        await git.CommitAmendAsync(all: true);

        var log3 = repo.RunGit("log -1 --pretty=%B").Trim();
        Assert.Equal("Amended doc version 2", log3);
        Assert.Equal("version 3", repo.ReadFile("doc.txt"));
    }

    [Fact]
    public async Task RevertAsync_RevertsTargetCommit()
    {
        using var repo = GitCliTestRepository.Create();
        var git = new GitCliRepository(repo.WorkingDirectory);

        repo.Commit("Feature commit", ("feature.txt", "some feature"));
        Assert.True(File.Exists(Path.Combine(repo.WorkingDirectory, "feature.txt")));

        var featureCommit = repo.RunGit("rev-parse HEAD").Trim();
        await git.RevertAsync(featureCommit);

        Assert.False(File.Exists(Path.Combine(repo.WorkingDirectory, "feature.txt")));
        var log = repo.RunGit("log -1 --pretty=%B");
        Assert.Contains("Revert", log);
    }

    [Fact]
    public async Task RestoreFileAsync_RestoresModifiedFile()
    {
        using var repo = GitCliTestRepository.Create();
        var git = new GitCliRepository(repo.WorkingDirectory);

        repo.WriteFile("README.md", "mutated dirty content");
        Assert.Equal("mutated dirty content", repo.ReadFile("README.md"));

        await git.RestoreFileAsync("README.md");

        Assert.Equal("seed", repo.ReadFile("README.md"));
    }

    [Fact]
    public async Task GetCommitStatAsync_And_GetDiffAsync_ReturnAccurateResults()
    {
        using var repo = GitCliTestRepository.Create();
        var git = new GitCliRepository(repo.WorkingDirectory);

        repo.Commit("Update readme", ("README.md", "line 1\nline 2\nline 3\n"));
        var head = repo.RunGit("rev-parse HEAD").Trim();

        var stat = await git.GetCommitStatAsync(head);
        Assert.Equal(1, stat.FilesChanged);
        Assert.True(stat.Insertions > 0);

        var diff = await git.GetCommitDiffAsync(head);
        Assert.Contains("line 1", diff);
        Assert.Contains("line 2", diff);

        var rangeDiff = await git.GetDiffAsync("HEAD~1", "HEAD");
        Assert.Contains("+line 1", rangeDiff);
    }
}
