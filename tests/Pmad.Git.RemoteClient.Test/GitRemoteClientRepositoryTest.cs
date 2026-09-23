using Pmad.Git.LocalRepositories;
using Pmad.Git.RemoteClient;

namespace Pmad.Git.RemoteClient.Test;

public sealed class GitRemoteClientRepositoryTest : IDisposable
{
    private readonly string _workingDir;

    public GitRemoteClientRepositoryTest()
    {
        _workingDir = Path.Combine(Path.GetTempPath(), "PmadGitClientRepoTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workingDir);
    }

    [Fact]
    public void Properties_InitializedCorrectly()
    {
        var repo = GitRepositoryWithIndexAndWorkspace.Init(_workingDir);
        var options = new GitRemoteClientOptions { Agent = "custom-agent/2.0" };

        using var clientRepo = new GitRemoteClientRepository(repo, "http://localhost/test.git", options);

        Assert.Equal(_workingDir, clientRepo.RootPath);
        Assert.NotNull(clientRepo.LocalRepository);
        Assert.NotNull(clientRepo.WorkspaceRepository);
        Assert.Equal("http://localhost/test.git", clientRepo.DefaultRemoteUrl);
        Assert.Equal("custom-agent/2.0", clientRepo.Options.Agent);
    }

    [Fact]
    public async Task TrackingMethods_DelegateToLocalRepository()
    {
        var repo = GitRepositoryWithIndexAndWorkspace.Init(_workingDir);
        File.WriteAllText(Path.Combine(_workingDir, "file.txt"), "hello");
        await repo.StageAsync("file.txt");
        var commit = await repo.CommitAsync("Initial commit");

        using var clientRepo = new GitRemoteClientRepository(repo);

        var status = await clientRepo.GetTrackingStatusAsync();
        Assert.Equal("main", status.LocalBranch);
        Assert.Null(status.UpstreamBranch);

        var isPushed = await clientRepo.IsCommitPushedAsync(commit.Value);
        Assert.False(isPushed);

        var isPushedHash = await clientRepo.IsCommitPushedAsync(commit);
        Assert.False(isPushedHash);
    }

    [Fact]
    public async Task WorkspaceDelegation_MergeInProgressAndConflicts()
    {
        var repo = GitRepositoryWithIndexAndWorkspace.Init(_workingDir);
        using var clientRepo = new GitRemoteClientRepository(repo);

        Assert.False(await clientRepo.IsMergeInProgressAsync());
        Assert.Empty(await clientRepo.GetConflictedFilesAsync());
    }

    public void Dispose()
    {
        TestHelper.TryDeleteDirectory(_workingDir);
    }
}
