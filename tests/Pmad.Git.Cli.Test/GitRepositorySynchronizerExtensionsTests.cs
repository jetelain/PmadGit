using Pmad.Git.Cli.Test.Infrastructure;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.Cli.Test;

/// <summary>
/// Unit tests for <see cref="GitRepositorySynchronizerExtensions.CreateSynchronizer"/>.
/// </summary>
public sealed class GitRepositorySynchronizerExtensionsTests : IDisposable
{
    private readonly string _repoPath;
    private readonly GitRepository _repository;

    public GitRepositorySynchronizerExtensionsTests()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), $"sync-ext-test-{Guid.NewGuid():N}");
        _repository = GitRepository.Init(_repoPath);
    }

    public void Dispose()
    {
        TestHelper.TryDeleteDirectory(_repoPath);
    }

    [Fact]
    public async Task CreateSynchronizer_Starts_By_Default()
    {
        var runner = new FakeGitRunner();
        var options = new GitSyncOptions { GitRunner = runner };

        await using var synchronizer = _repository.CreateSynchronizer(options);

        Assert.Equal(GitSyncState.Idle, synchronizer.State);

        // Starting again through Start() must be a no-op (idempotent), proving Start() was
        // already called by CreateSynchronizer.
        synchronizer.Start();
    }

    [Fact]
    public async Task CreateSynchronizer_With_Start_False_Does_Not_Start_Periodic_Loop()
    {
        var runner = new FakeGitRunner();
        var options = new GitSyncOptions { GitRunner = runner };

        await using var synchronizer = _repository.CreateSynchronizer(options, start: false);

        Assert.Equal(GitSyncState.Idle, synchronizer.State);
    }

    [Fact]
    public async Task CreateSynchronizer_Subscribes_To_Repository_Changed_Event()
    {
        var runner = new FakeGitRunner().Enqueue(0);
        var options = new GitSyncOptions { GitRunner = runner, PushDebounceDelay = TimeSpan.FromMilliseconds(20) };

        await using var synchronizer = _repository.CreateSynchronizer(options, start: false);

        _repository.InvalidateCaches();

        var start = DateTime.UtcNow;
        while (runner.Calls.Count == 0 && (DateTime.UtcNow - start).TotalMilliseconds < 2000)
        {
            await Task.Delay(10);
        }

        Assert.Equal(new[] { "push" }, runner.Calls.Single());
    }
}
