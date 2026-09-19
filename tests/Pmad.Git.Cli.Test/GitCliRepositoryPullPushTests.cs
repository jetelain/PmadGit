using Pmad.Git.Cli.Test.Infrastructure;

namespace Pmad.Git.Cli.Test;

public class GitCliRepositoryPullPushTests
{
    [Fact]
    public async Task PushAsync_Then_PullAsync_Synchronizes_Repositories()
    {
        using var remote = GitCliTestRepository.CreateBare();
        using var repoA = GitCliTestRepository.Create();
        repoA.AddRemote("origin", remote);

        var gitA = new GitCliRepository(repoA.WorkingDirectory);
        await gitA.PushAsync("origin", GitCliTestRepository.DefaultBranch, setUpstream: true);

        using var repoB = GitCliTestRepository.Clone(remote);
        repoB.Commit("Change from B", ("fromB.txt", "content"));
        var gitB = new GitCliRepository(repoB.WorkingDirectory);
        await gitB.PushAsync();

        var pullResult = await gitA.PullAsync();

        Assert.True(pullResult.IsSuccess);
        Assert.False(pullResult.HasConflicts);
        Assert.True(File.Exists(Path.Combine(repoA.WorkingDirectory, "fromB.txt")));
    }

    [Fact]
    public async Task PullAsync_Returns_Conflicts_When_Changes_Overlap()
    {
        using var remote = GitCliTestRepository.CreateBare();
        using var repoA = GitCliTestRepository.Create();
        repoA.AddRemote("origin", remote);

        var gitA = new GitCliRepository(repoA.WorkingDirectory);
        await gitA.PushAsync("origin", GitCliTestRepository.DefaultBranch, setUpstream: true);

        using var repoB = GitCliTestRepository.Clone(remote);
        repoB.Commit("Change from B", ("README.md", "from B"));
        var gitB = new GitCliRepository(repoB.WorkingDirectory);
        await gitB.PushAsync();

        repoA.Commit("Change from A", ("README.md", "from A"));

        var pullResult = await gitA.PullAsync();

        Assert.False(pullResult.IsSuccess);
        Assert.True(pullResult.HasConflicts);
        Assert.Contains("README.md", pullResult.ConflictedFiles);

        await gitA.AbortMergeAsync();
        Assert.False(await gitA.IsMergeInProgressAsync());
    }

    [Fact]
    public async Task PushAsync_Without_Remote_Uses_Configured_Upstream()
    {
        using var remote = GitCliTestRepository.CreateBare();
        using var repoA = GitCliTestRepository.Create();
        repoA.AddRemote("origin", remote);

        var gitA = new GitCliRepository(repoA.WorkingDirectory);
        await gitA.PushAsync("origin", GitCliTestRepository.DefaultBranch, setUpstream: true);

        repoA.Commit("Another change", ("another.txt", "content"));
        await gitA.PushAsync();

        var log = repoA.RunGit($"log origin/{GitCliTestRepository.DefaultBranch} --oneline");
        Assert.Contains("Another change", log);
    }
}
