using Pmad.Git.Cli.Test.Infrastructure;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.Cli.Test;

/// <summary>
/// Unit tests that exercise <see cref="GitRepositorySynchronizer"/> in isolation using a
/// <see cref="FakeGitRunner"/> and a real (but disk-backed) <see cref="GitRepository"/> so that
/// its <see cref="IGitRepositoryCacheInvalidator.Changed"/> event can be observed without invoking
/// the real <c>git</c> executable for push/pull/merge operations.
/// </summary>
public sealed class GitRepositorySynchronizerUnitTests : IDisposable
{
    private readonly string _repoPath;
    private readonly GitRepository _repository;

    public GitRepositorySynchronizerUnitTests()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), $"sync-unit-test-{Guid.NewGuid():N}");
        _repository = GitRepository.Init(_repoPath);
    }

    public void Dispose()
    {
        TestHelper.TryDeleteDirectory(_repoPath);
    }

    private GitSyncOptions CreateOptions(FakeGitRunner runner, TimeSpan? pushDebounceDelay = null)
    {
        return new GitSyncOptions
        {
            GitRunner = runner,
            PushDebounceDelay = pushDebounceDelay ?? TimeSpan.FromMilliseconds(20),
            PullInterval = TimeSpan.FromHours(1),
        };
    }

    [Fact]
    public void State_Is_Idle_By_Default()
    {
        var runner = new FakeGitRunner();
        var synchronizer = new GitRepositorySynchronizer(_repository, CreateOptions(runner));

        Assert.Equal(GitSyncState.Idle, synchronizer.State);
        Assert.Null(synchronizer.Conflict);
    }

    [Fact]
    public async Task NotifyLocalChange_Then_Debounce_Elapses_Triggers_Push()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        await using var synchronizer = new GitRepositorySynchronizer(_repository, CreateOptions(runner));

        synchronizer.NotifyLocalChange();

        await WaitUntilAsync(() => runner.Calls.Count > 0);

        Assert.Equal(new[] { "push" }, runner.Calls.Single());
    }

    [Fact]
    public async Task RepositoryChanged_Event_Schedules_Debounced_Push()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        await using var synchronizer = new GitRepositorySynchronizer(_repository, CreateOptions(runner));

        // Simulate an external local change (e.g. a commit made through IGitRepository).
        _repository.InvalidateCaches();

        await WaitUntilAsync(() => runner.Calls.Count > 0);

        Assert.Equal(new[] { "push" }, runner.Calls.Single());
    }

    [Fact]
    public async Task FlushPendingPushAsync_Without_Pending_Change_Does_Nothing()
    {
        var runner = new FakeGitRunner();
        await using var synchronizer = new GitRepositorySynchronizer(_repository, CreateOptions(runner));

        await synchronizer.FlushPendingPushAsync();

        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task FlushPendingPushAsync_Pushes_Immediately_And_Cancels_Debounce()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        await using var synchronizer = new GitRepositorySynchronizer(_repository, CreateOptions(runner, TimeSpan.FromMinutes(5)));

        synchronizer.NotifyLocalChange();
        await synchronizer.FlushPendingPushAsync();

        Assert.Equal(new[] { "push" }, runner.Calls.Single());
    }

    [Fact]
    public async Task Push_Triggered_By_Synchronizer_Does_Not_Reschedule_Itself()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        await using var synchronizer = new GitRepositorySynchronizer(_repository, CreateOptions(runner));

        synchronizer.NotifyLocalChange();
        await synchronizer.FlushPendingPushAsync();

        // The push above invalidates the repository's caches (self-triggered "Changed"), which
        // must not schedule another push.
        await Task.Delay(50);

        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task TriggerRemoteSyncAsync_Successful_Pull_Keeps_State_Idle()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        await using var synchronizer = new GitRepositorySynchronizer(_repository, CreateOptions(runner));

        await synchronizer.TriggerRemoteSyncAsync();

        Assert.Equal(new[] { "pull" }, runner.Calls.Single());
        Assert.Equal(GitSyncState.Idle, synchronizer.State);
        Assert.Null(synchronizer.Conflict);
    }

    [Fact]
    public async Task TriggerRemoteSyncAsync_Conflict_Sets_ConflictState()
    {
        var runner = new FakeGitRunner()
            .Enqueue(1, stderr: "CONFLICT")
            .Enqueue(0, stdout: "README.md\n");
        await using var synchronizer = new GitRepositorySynchronizer(_repository, CreateOptions(runner));

        await synchronizer.TriggerRemoteSyncAsync();

        Assert.Equal(GitSyncState.Conflict, synchronizer.State);
        Assert.NotNull(synchronizer.Conflict);
        Assert.Equal(new[] { "README.md" }, synchronizer.Conflict!.ConflictedFiles);
    }

    [Fact]
    public async Task TriggerRemoteSyncAsync_Does_Nothing_While_Conflict_Pending()
    {
        var runner = new FakeGitRunner()
            .Enqueue(1, stderr: "CONFLICT")
            .Enqueue(0, stdout: "README.md\n");
        await using var synchronizer = new GitRepositorySynchronizer(_repository, CreateOptions(runner));

        await synchronizer.TriggerRemoteSyncAsync();
        Assert.Equal(GitSyncState.Conflict, synchronizer.State);

        await synchronizer.TriggerRemoteSyncAsync();

        // No additional git calls were made for the second attempt.
        Assert.Equal(2, runner.Calls.Count);
    }

    [Fact]
    public async Task ResolveConflictAsync_Without_Pending_Conflict_Throws()
    {
        var runner = new FakeGitRunner();
        await using var synchronizer = new GitRepositorySynchronizer(_repository, CreateOptions(runner));

        await Assert.ThrowsAsync<InvalidOperationException>(() => synchronizer.ResolveConflictAsync("README.md"));
    }

    [Fact]
    public async Task CompleteConflictResolutionAsync_Clears_Conflict_And_Pushes()
    {
        var runner = new FakeGitRunner()
            .Enqueue(1, stderr: "CONFLICT")
            .Enqueue(0, stdout: "README.md\n")
            .Enqueue(0) // add --
            .Enqueue(0) // commit
            .Enqueue(0); // push
        await using var synchronizer = new GitRepositorySynchronizer(_repository, CreateOptions(runner));

        await synchronizer.TriggerRemoteSyncAsync();
        Assert.Equal(GitSyncState.Conflict, synchronizer.State);

        await synchronizer.ResolveConflictAsync("README.md");
        await synchronizer.CompleteConflictResolutionAsync("Merge resolved");

        await WaitUntilAsync(() => synchronizer.State == GitSyncState.Idle);

        Assert.Null(synchronizer.Conflict);
        Assert.Equal(GitSyncState.Idle, synchronizer.State);
        Assert.Contains(runner.Calls, c => c[0] == "push");
    }

    [Fact]
    public async Task AbortConflictResolutionAsync_Clears_Conflict()
    {
        var runner = new FakeGitRunner()
            .Enqueue(1, stderr: "CONFLICT")
            .Enqueue(0, stdout: "README.md\n")
            .Enqueue(0); // merge --abort
        await using var synchronizer = new GitRepositorySynchronizer(_repository, CreateOptions(runner));

        await synchronizer.TriggerRemoteSyncAsync();
        Assert.Equal(GitSyncState.Conflict, synchronizer.State);

        await synchronizer.AbortConflictResolutionAsync();

        Assert.Null(synchronizer.Conflict);
        Assert.Equal(GitSyncState.Idle, synchronizer.State);
    }

    [Fact]
    public async Task Start_Is_Idempotent()
    {
        var runner = new FakeGitRunner();
        await using var synchronizer = new GitRepositorySynchronizer(_repository, CreateOptions(runner));

        synchronizer.Start();
        synchronizer.Start();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        var start = DateTime.UtcNow;
        while (!condition())
        {
            if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException("Condition was not met within the expected delay.");
            }
            await Task.Delay(10);
        }
    }
}
