using Pmad.Git.Cli.Test.Infrastructure;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.Cli.Test;

/// <summary>
/// Unit tests verifying that <see cref="GitCliRepository"/> notifies the shared cache invalidator
/// (via <see cref="IGitRepositoryCacheInvalidator.Changed"/>) even when a merge/pull operation
/// stops because of conflicts, using a <see cref="FakeGitRunner"/> combined with a real
/// disk-backed <see cref="GitRepository"/> so that the event can be observed without invoking the
/// real git executable.
/// </summary>
public sealed class GitCliRepositoryCacheInvalidationTests : IDisposable
{
    private readonly string _repoPath;
    private readonly GitRepository _repository;

    public GitCliRepositoryCacheInvalidationTests()
    {
        _repoPath = Path.Combine(Path.GetTempPath(), $"cli-cache-invalidation-test-{Guid.NewGuid():N}");
        _repository = GitRepository.Init(_repoPath);
    }

    public void Dispose()
    {
        TestHelper.TryDeleteDirectory(_repoPath);
    }

    [Fact]
    public async Task MergeAsync_Conflict_Invalidates_Caches()
    {
        var runner = new FakeGitRunner()
            .Enqueue(1, stderr: "CONFLICT")
            .Enqueue(0, stdout: "README.md\n");
        var cli = new GitCliRepository(_repository, runner);

        var changedRaised = false;
        _repository.Changed += (_, _) => changedRaised = true;

        var result = await cli.MergeAsync("feature");

        Assert.True(result.HasConflicts);
        Assert.True(changedRaised);
    }

    [Fact]
    public async Task PullAsync_Conflict_Invalidates_Caches()
    {
        var runner = new FakeGitRunner()
            .Enqueue(1, stderr: "CONFLICT")
            .Enqueue(0, stdout: "README.md\n");
        var cli = new GitCliRepository(_repository, runner);

        var changedRaised = false;
        _repository.Changed += (_, _) => changedRaised = true;

        var result = await cli.PullAsync();

        Assert.True(result.HasConflicts);
        Assert.True(changedRaised);
    }
}
