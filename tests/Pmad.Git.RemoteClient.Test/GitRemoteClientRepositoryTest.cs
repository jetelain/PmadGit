using System.Net;
using Pmad.Git.LocalRepositories;
using Pmad.Git.LocalRepositories.Config;
using Pmad.Git.Protocol;
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

    [Fact]
    public async Task PushAsync_HoldsReferenceLock_BlocksConcurrentCommit()
    {
        var repo = GitRepositoryWithIndexAndWorkspace.Init(_workingDir);
        File.WriteAllText(Path.Combine(_workingDir, "file.txt"), "hello");
        await repo.StageAsync("file.txt");
        var firstCommit = await repo.CommitAsync("Initial commit");

        var pushPausedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pushResumeTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var handler = new MockHttpMessageHandler
        {
            Handler = async req =>
            {
                if (req.RequestUri!.PathAndQuery.Contains("info/refs"))
                {
                    var body = new MemoryStream();
                    await PktLineWriter.WriteStringAsync(body, "# service=git-receive-pack\n", CancellationToken.None);
                    await PktLineWriter.WriteFlushAsync(body, CancellationToken.None);
                    await PktLineWriter.WriteStringAsync(body, "0000000000000000000000000000000000000000 capabilities^{}\0report-status\n", CancellationToken.None);
                    await PktLineWriter.WriteFlushAsync(body, CancellationToken.None);
                    body.Seek(0, SeekOrigin.Begin);

                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(body)
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-git-receive-pack-advertisement");
                    return response;
                }
                else
                {
                    // Inside receive-pack POST: signal that push is in-flight and hold until resumed
                    pushPausedTcs.TrySetResult();
                    await pushResumeTcs.Task;

                    var body = new MemoryStream();
                    await PktLineWriter.WriteStringAsync(body, "unpack ok\n", CancellationToken.None);
                    await PktLineWriter.WriteStringAsync(body, "ok refs/heads/main\n", CancellationToken.None);
                    await PktLineWriter.WriteFlushAsync(body, CancellationToken.None);
                    body.Seek(0, SeekOrigin.Begin);

                    var response = new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StreamContent(body)
                    };
                    response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-git-receive-pack-result");
                    return response;
                }
            }
        };

        var httpClient = new HttpClient(handler);
        var options = new GitRemoteClientOptions { HttpClient = httpClient };
        using var clientRepo = new GitRemoteClientRepository(repo, "http://localhost/test.git", options);

        var pushTask = Task.Run(async () => await clientRepo.PushAsync());

        // Wait until push is inside the HTTP receive-pack call (holding the reference lock)
        await pushPausedTcs.Task;

        // Try to commit concurrently on the same branch
        File.WriteAllText(Path.Combine(_workingDir, "file.txt"), "updated during push");
        await repo.StageAsync("file.txt");
        var commitTask = Task.Run(async () => await repo.CommitAsync("Concurrent commit"));

        // Give a short delay to verify commitTask cannot complete while lock is held
        var completed = await Task.WhenAny(commitTask, Task.Delay(150));
        Assert.NotSame(commitTask, completed);
        Assert.False(commitTask.IsCompleted);

        // Resume push
        pushResumeTcs.TrySetResult();

        // Now both should complete successfully
        await pushTask;
        var secondCommit = await commitTask;

        Assert.NotEqual(firstCommit, secondCommit);
    }

    public void Dispose()
    {
        TestHelper.TryDeleteDirectory(_workingDir);
    }
}
