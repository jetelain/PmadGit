using Pmad.Git.LocalRepositories;
using Pmad.Git.LocalRepositories.Config;
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

    [Theory]
    [InlineData("bad\"name")]
    [InlineData("bad\nname")]
    [InlineData("bad\rname")]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    [InlineData("bad name")]
    [InlineData("..")]
    [InlineData(".")]
    [InlineData(".lock")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoteName_Validation_RejectsInvalidNames(string invalidName)
    {
        var repo = GitRepositoryWithIndexAndWorkspace.Init(_workingDir);
        using var clientRepo = new GitRemoteClientRepository(repo);

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await clientRepo.FetchAsync(remote: invalidName);
        });

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await clientRepo.PushAsync(remote: invalidName);
        });

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await clientRepo.PullAsync(remote: invalidName);
        });

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await GitRemoteClientRepository.CloneAsync("http://localhost/test.git", Path.Combine(_workingDir, "clone"), remoteName: invalidName);
        });
    }

    [Theory]
    [InlineData("ssh://git@github.com/owner/repo.git")]
    [InlineData("git@github.com:owner/repo.git")]
    [InlineData("file:///C:/repos/test.git")]
    [InlineData("ftp://example.com/repo.git")]
    public async Task CloneAsync_UnsupportedScheme_ThrowsNotSupportedException(string unsupportedUrl)
    {
        var targetDir = Path.Combine(_workingDir, "clone-unsupported");
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await GitRemoteClientRepository.CloneAsync(unsupportedUrl, targetDir);
        });
    }

    [Theory]
    [InlineData("refs/tags/v1.0")]
    [InlineData("refs/remotes/origin/main")]
    [InlineData("refs/pull/1/head")]
    public async Task CloneAsync_NonHeadRef_ThrowsArgumentException(string nonHeadRef)
    {
        var targetDir = Path.Combine(_workingDir, "clone-ref");
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await GitRemoteClientRepository.CloneAsync("http://localhost/test.git", targetDir, branch: nonHeadRef);
        });
    }

    [Fact]
    public async Task PushAsync_EmptyBranchWithoutCommit_ThrowsInvalidOperationException()
    {
        var repo = GitRepositoryWithIndexAndWorkspace.Init(_workingDir);
        using var clientRepo = new GitRemoteClientRepository(repo, "http://localhost/test.git");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await clientRepo.PushAsync();
        });

        Assert.Contains("has no commits to push", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_UnsupportedConfiguredRemoteUrl_ThrowsNotSupportedException()
    {
        var repo = GitRepositoryWithIndexAndWorkspace.Init(_workingDir);
        var configPath = Path.Combine(repo.GitDirectory, "config");
        var config = await GitConfigFile.ReadFromFileAsync(configPath);
        config.SetValue("remote", "origin", "url", "git@github.com:owner/repo.git");
        await config.WriteToFileAsync(configPath);

        using var clientRepo = new GitRemoteClientRepository(repo);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await clientRepo.FetchAsync();
        });

        Assert.Contains("only supports HTTP and HTTPS", ex.Message);
    }

    [Fact]
    public async Task PullAsync_RebaseTrue_ThrowsNotSupportedException()
    {
        var repo = GitRepositoryWithIndexAndWorkspace.Init(_workingDir);
        using var clientRepo = new GitRemoteClientRepository(repo);

        await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await clientRepo.PullAsync(rebase: true);
        });
    }

    [Fact]
    public async Task PullAsync_BareRepoWithoutWorkspace_ThrowsInvalidOperationException()
    {
        var repo = GitRepository.Init(_workingDir, bare: true);
        using var clientRepo = new GitRemoteClientRepository(repo);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await clientRepo.PullAsync();
        });
    }

    public void Dispose()
    {
        TestHelper.TryDeleteDirectory(_workingDir);
    }
}
