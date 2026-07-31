using Pmad.Git.Cli.Test.Infrastructure;

namespace Pmad.Git.Cli.Test;

/// <summary>
/// Integration tests that exercise <see cref="GitRepositorySynchronizer"/> and
/// <see cref="GitRepositorySynchronizerExtensions.CreateSynchronizer"/> against real repositories
/// backed by the real <c>git</c> executable.
/// </summary>
public class GitRepositorySynchronizerIntegrationTests
{
    [Fact]
    public async Task FlushPendingPushAsync_Pushes_Local_Commit_To_Remote()
    {
        using var remote = GitCliTestRepository.CreateBare();
        using var repoA = GitCliTestRepository.Create();
        repoA.AddRemote("origin", remote);

        var gitA = new GitCliRepository(repoA.WorkingDirectory);
        await gitA.PushAsync("origin", GitCliTestRepository.DefaultBranch, setUpstream: true);

        var options = new GitSyncOptions { PushDebounceDelay = TimeSpan.FromMinutes(5) };
        await using var synchronizer = new GitRepositorySynchronizer(gitA, options);

        repoA.Commit("Local change", ("local.txt", "content"));
        synchronizer.NotifyLocalChange();

        await synchronizer.FlushPendingPushAsync();

        using var repoB = GitCliTestRepository.Clone(remote);
        Assert.True(File.Exists(Path.Combine(repoB.WorkingDirectory, "local.txt")));
    }

    [Fact]
    public async Task TriggerRemoteSyncAsync_Pulls_Remote_Changes()
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

        await using var synchronizer = new GitRepositorySynchronizer(gitA);

        await synchronizer.TriggerRemoteSyncAsync();

        Assert.Equal(GitSyncState.Idle, synchronizer.State);
        Assert.True(File.Exists(Path.Combine(repoA.WorkingDirectory, "fromB.txt")));
    }

    [Fact]
    public async Task TriggerRemoteSyncAsync_With_Conflict_Allows_Resolution_And_Push()
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

        await using var synchronizer = new GitRepositorySynchronizer(gitA);

        await synchronizer.TriggerRemoteSyncAsync();

        Assert.Equal(GitSyncState.Conflict, synchronizer.State);
        Assert.NotNull(synchronizer.Conflict);
        Assert.Contains("README.md", synchronizer.Conflict!.ConflictedFiles);

        File.WriteAllText(Path.Combine(repoA.WorkingDirectory, "README.md"), "resolved");
        await synchronizer.ResolveConflictAsync("README.md");
        await synchronizer.CompleteConflictResolutionAsync("Resolve conflict");

        Assert.Equal(GitSyncState.Idle, synchronizer.State);
        Assert.Null(synchronizer.Conflict);

        using var repoC = GitCliTestRepository.Clone(remote);
        Assert.Equal("resolved", repoC.ReadFile("README.md"));
    }

    [Fact]
    public async Task TriggerRemoteSyncAsync_With_Conflict_Can_Be_Aborted()
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

        await using var synchronizer = new GitRepositorySynchronizer(gitA);
        await synchronizer.TriggerRemoteSyncAsync();

        Assert.Equal(GitSyncState.Conflict, synchronizer.State);

        await synchronizer.AbortConflictResolutionAsync();

        Assert.Equal(GitSyncState.Idle, synchronizer.State);
        Assert.Null(synchronizer.Conflict);
        Assert.False(await gitA.IsMergeInProgressAsync());
    }

    [Fact]
    public async Task Start_Runs_Periodic_Pull_Loop()
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

        var options = new GitSyncOptions { PullInterval = TimeSpan.FromMilliseconds(50) };
        await using var synchronizer = new GitRepositorySynchronizer(gitA, options);
        synchronizer.Start();

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(Path.Combine(repoA.WorkingDirectory, "fromB.txt")) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(50);
        }

        Assert.True(File.Exists(Path.Combine(repoA.WorkingDirectory, "fromB.txt")));
    }
}
