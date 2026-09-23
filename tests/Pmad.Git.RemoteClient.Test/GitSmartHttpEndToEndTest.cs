using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pmad.Git.HttpServer;
using Pmad.Git.LocalRepositories;
using Pmad.Git.LocalRepositories.Config;
using Pmad.Git.RemoteClient;

namespace Pmad.Git.RemoteClient.Test;

public sealed class GitSmartHttpEndToEndTest : IDisposable
{
    private readonly string _serverRepoRoot;
    private readonly string _clientWorkingDir;
    private IHost? _host;
    private TestServer? _testServer;

    public GitSmartHttpEndToEndTest()
    {
        _serverRepoRoot = Path.Combine(Path.GetTempPath(), "PmadGitE2EServer", Guid.NewGuid().ToString("N"));
        _clientWorkingDir = Path.Combine(Path.GetTempPath(), "PmadGitE2EClient", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_serverRepoRoot);
        Directory.CreateDirectory(_clientWorkingDir);
    }

    private async Task StartServerAsync()
    {
        var builder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddGitSmartHttp(options =>
                        {
                            options.RepositoryRoot = _serverRepoRoot;
                            options.EnableUploadPack = true;
                            options.EnableReceivePack = true;
                            // Allow both read and write operations for tests
                            options.AuthorizeAsync = static (_, _, _, _) => ValueTask.FromResult(true);
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints.MapGitSmartHttp("/{repository}.git");
                        });
                    });
            });

        _host = await builder.StartAsync();
        _testServer = _host.GetTestServer();
    }

    private void CreateServerRepository(string name, (string path, string content)[] files, GitObjectFormat objectFormat = GitObjectFormat.Sha1)
    {
        var barePath = Path.Combine(_serverRepoRoot, $"{name}.git");
        using var repo = GitRepositoryWithIndexAndWorkspace.Init(barePath, initialBranch: "main", objectFormat: objectFormat);

        foreach (var (path, content) in files)
        {
            var fullPath = Path.Combine(barePath, path.Replace('/', Path.DirectorySeparatorChar));
            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(fullPath, content);
            repo.StageAsync(path).GetAwaiter().GetResult();
        }

        repo.CommitAsync("Initial commit").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task CloneAsync_WithFiles_ClonesRepositoryAndChecksOutFiles()
    {
        // Arrange
        CreateServerRepository("clone-test", new[]
        {
            ("README.md", "# Test Readme"),
            ("src/app.txt", "console.log('hello');")
        });

        await StartServerAsync();
        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "cloned-repo");

        // Act
        using var repo = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/clone-test.git",
            cloneDir,
            options);

        // Assert
        Assert.NotNull(repo);
        Assert.True(File.Exists(Path.Combine(cloneDir, "README.md")));
        Assert.True(File.Exists(Path.Combine(cloneDir, "src", "app.txt")));
        Assert.Equal("# Test Readme", File.ReadAllText(Path.Combine(cloneDir, "README.md")));
        Assert.Equal("console.log('hello');", File.ReadAllText(Path.Combine(cloneDir, "src", "app.txt")));

        var trackingRef = await repo.LocalRepository.ReferenceStore.TryResolveReferenceAsync("refs/remotes/origin/main");
        var headRef = await repo.LocalRepository.ReferenceStore.TryResolveReferenceAsync("refs/heads/main");
        Assert.NotNull(trackingRef);
        Assert.NotNull(headRef);
        Assert.Equal(trackingRef, headRef);
    }

    [Fact]
    public async Task FetchAsync_ServerHasNewCommits_UpdatesTrackingRefsAndObjectStore()
    {
        // Arrange: clone initial repository
        CreateServerRepository("fetch-test", new[] { ("file1.txt", "v1") });
        await StartServerAsync();

        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "fetch-clone");

        using var clientRepo = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/fetch-test.git",
            cloneDir,
            options);

        var initialCommit = await clientRepo.LocalRepository.ReferenceStore.ResolveHeadAsync();

        // Make another commit on the server directly
        var serverRepoPath = Path.Combine(_serverRepoRoot, "fetch-test.git");
        using (var serverRepo = GitRepositoryWithIndexAndWorkspace.Open(serverRepoPath))
        {
            File.WriteAllText(Path.Combine(serverRepoPath, "file2.txt"), "v2 content");
            await serverRepo.StageAsync("file2.txt");
            await serverRepo.CommitAsync("Second server commit");
        }

        // Act: Fetch on client
        await clientRepo.FetchAsync();

        // Assert: Tracking ref should be updated to new commit
        var updatedTrackingRef = await clientRepo.LocalRepository.ReferenceStore.TryResolveReferenceAsync("refs/remotes/origin/main");
        Assert.NotNull(updatedTrackingRef);
        Assert.NotEqual(initialCommit, updatedTrackingRef.Value);

        // Verify the new commit and blob exist in local object store
        var commitObj = await clientRepo.LocalRepository.ObjectStore.ReadObjectAsync(updatedTrackingRef.Value);
        Assert.Equal(GitObjectType.Commit, commitObj.Type);

        // Working tree should NOT have file2.txt yet (fetch doesn't merge)
        Assert.False(File.Exists(Path.Combine(cloneDir, "file2.txt")));
    }

    [Fact]
    public async Task PullAsync_ServerHasNewCommits_MergesIntoWorkingTree()
    {
        // Arrange
        CreateServerRepository("pull-test", new[] { ("file1.txt", "v1") });
        await StartServerAsync();

        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "pull-clone");

        using var clientRepo = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/pull-test.git",
            cloneDir,
            options);

        // Make another commit on the server
        var serverRepoPath = Path.Combine(_serverRepoRoot, "pull-test.git");
        using (var serverRepo = GitRepositoryWithIndexAndWorkspace.Open(serverRepoPath))
        {
            File.WriteAllText(Path.Combine(serverRepoPath, "file2.txt"), "v2 from server");
            await serverRepo.StageAsync("file2.txt");
            await serverRepo.CommitAsync("Commit 2");
        }

        // Act: Pull on client
        var mergeResult = await clientRepo.PullAsync();

        // Assert
        Assert.True(mergeResult.IsSuccess);
        Assert.Empty(mergeResult.ConflictedFiles);
        Assert.True(File.Exists(Path.Combine(cloneDir, "file2.txt")));
        Assert.Equal("v2 from server", File.ReadAllText(Path.Combine(cloneDir, "file2.txt")));
    }

    [Fact]
    public async Task PullAsync_WithMergeConflict_ReportsConflictAndAllowsResolution()
    {
        // Arrange
        CreateServerRepository("conflict-test", new[] { ("conflict.txt", "line 1\nline 2\n") });
        await StartServerAsync();

        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "conflict-clone");

        using var clientRepo = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/conflict-test.git",
            cloneDir,
            options);

        // Server modifies conflict.txt
        var serverRepoPath = Path.Combine(_serverRepoRoot, "conflict-test.git");
        using (var serverRepo = GitRepositoryWithIndexAndWorkspace.Open(serverRepoPath))
        {
            File.WriteAllText(Path.Combine(serverRepoPath, "conflict.txt"), "server modification\nline 2\n");
            await serverRepo.StageAsync("conflict.txt");
            await serverRepo.CommitAsync("Server edit");
        }

        // Client modifies conflict.txt concurrently
        File.WriteAllText(Path.Combine(cloneDir, "conflict.txt"), "client modification\nline 2\n");
        await clientRepo.WorkspaceRepository!.StageAsync("conflict.txt");
        await clientRepo.WorkspaceRepository!.CommitAsync("Client edit");

        // Act: Pull should encounter conflict
        var mergeResult = await clientRepo.PullAsync();

        // Assert: Merge conflict reported
        Assert.False(mergeResult.IsSuccess);
        Assert.Contains("conflict.txt", mergeResult.ConflictedFiles);
        Assert.True(await clientRepo.IsMergeInProgressAsync());

        var conflictedFiles = await clientRepo.GetConflictedFilesAsync();
        Assert.Contains("conflict.txt", conflictedFiles);

        // Resolve conflict
        File.WriteAllText(Path.Combine(cloneDir, "conflict.txt"), "resolved content\nline 2\n");
        await clientRepo.ResolveConflictAsync("conflict.txt");

        // Continue merge
        await clientRepo.ContinueMergeAsync("Merge resolution commit");

        Assert.False(await clientRepo.IsMergeInProgressAsync());
        Assert.Equal("resolved content\nline 2\n", File.ReadAllText(Path.Combine(cloneDir, "conflict.txt")));
    }

    [Fact]
    public async Task PushAsync_LocalCommits_UpdatesServerRepository()
    {
        // Arrange
        CreateServerRepository("push-test", new[] { ("file1.txt", "original") });
        await StartServerAsync();

        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "push-clone");

        using var clientRepo = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/push-test.git",
            cloneDir,
            options);

        // Make local commit
        File.WriteAllText(Path.Combine(cloneDir, "file1.txt"), "updated by client");
        File.WriteAllText(Path.Combine(cloneDir, "client-new.txt"), "brand new file");
        await clientRepo.WorkspaceRepository!.StageAllAsync();
        var localCommit = await clientRepo.WorkspaceRepository!.CommitAsync("Client new commit");

        // Act: Push
        await clientRepo.PushAsync();

        // Assert: Verify server repo has the new commit
        var serverRepoPath = Path.Combine(_serverRepoRoot, "push-test.git");
        var serverRepo = GitRepository.Open(serverRepoPath);
        serverRepo.InvalidateCaches();
        var serverMain = await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/heads/main");

        Assert.NotNull(serverMain);
        Assert.Equal(localCommit, serverMain.Value);

        // Verify objects are present on server
        var commitObj = await serverRepo.ObjectStore.ReadObjectAsync(localCommit);
        Assert.Equal(GitObjectType.Commit, commitObj.Type);

        // Tracking status should report 0 ahead, 0 behind
        var status = await clientRepo.GetTrackingStatusAsync();
        Assert.Equal(0, status.AheadCount);
        Assert.Equal(0, status.BehindCount);
    }

    [Fact]
    public async Task PushAsync_NewBranch_CreatesBranchOnServer()
    {
        // Arrange
        CreateServerRepository("push-branch-test", new[] { ("init.txt", "init") });
        await StartServerAsync();

        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "branch-clone");

        using var clientRepo = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/push-branch-test.git",
            cloneDir,
            options);

        // Create new local branch
        var headCommit = await clientRepo.LocalRepository.ReferenceStore.ResolveHeadAsync();
        await clientRepo.LocalRepository.ReferenceStore.CreateReferenceAsync("refs/heads/feature", headCommit, overwrite: true);

        // Switch to branch feature
        File.WriteAllText(Path.Combine(cloneDir, ".git", "HEAD"), "ref: refs/heads/feature\n");
        clientRepo.LocalRepository.InvalidateCaches();

        File.WriteAllText(Path.Combine(cloneDir, "feature.txt"), "feature content");
        await clientRepo.WorkspaceRepository!.StageAsync("feature.txt");
        var featureCommit = await clientRepo.WorkspaceRepository!.CommitAsync("Feature commit");

        // Act: Push new branch with setUpstream: true
        await clientRepo.PushAsync(branch: "feature", setUpstream: true);

        // Assert: Server now has refs/heads/feature
        var serverRepoPath = Path.Combine(_serverRepoRoot, "push-branch-test.git");
        var serverRepo = GitRepository.Open(serverRepoPath);
        serverRepo.InvalidateCaches();
        var serverFeature = await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/heads/feature");

        Assert.NotNull(serverFeature);
        Assert.Equal(featureCommit, serverFeature.Value);

        // Local tracking ref created
        var localTracking = await clientRepo.LocalRepository.ReferenceStore.TryResolveReferenceAsync("refs/remotes/origin/feature");
        Assert.NotNull(localTracking);
        Assert.Equal(featureCommit, localTracking.Value);

        // Upstream tracking status
        var trackingStatus = await clientRepo.GetTrackingStatusAsync("feature");
        Assert.Equal("origin/feature", trackingStatus.UpstreamBranch);
        Assert.Equal(0, trackingStatus.AheadCount);
        Assert.Equal(0, trackingStatus.BehindCount);
    }

    [Fact]
    public async Task GitRepositorySynchronizer_WithRemoteClientRepository_SynchronizesChanges()
    {
        // Arrange
        CreateServerRepository("sync-test", new[] { ("file.txt", "v1") });
        await StartServerAsync();

        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "sync-clone");

        using var clientRepo = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/sync-test.git",
            cloneDir,
            options);

        await using var synchronizer = new GitRepositorySynchronizer(
            clientRepo,
            (IGitRepositoryCacheInvalidator)clientRepo.LocalRepository,
            new GitSyncOptions
            {
                PushDebounceDelay = TimeSpan.FromMilliseconds(50),
                PullInterval = TimeSpan.FromMilliseconds(200)
            });

        synchronizer.Start();

        // Act 1: Make local commit -> should automatically flush / push via synchronizer
        File.WriteAllText(Path.Combine(cloneDir, "file.txt"), "v2 local");
        await clientRepo.WorkspaceRepository!.StageAsync("file.txt");
        var localCommit = await clientRepo.WorkspaceRepository!.CommitAsync("v2 commit");

        await synchronizer.FlushPendingPushAsync();

        // Assert 1: Server has local commit
        var serverRepoPath = Path.Combine(_serverRepoRoot, "sync-test.git");
        var serverRepo = GitRepository.Open(serverRepoPath);
        serverRepo.InvalidateCaches();
        var serverMain = await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/heads/main");
        Assert.Equal(localCommit, serverMain);

        // Act 2: Server commit -> triggered remote pull
        using (var serverWorkRepo = GitRepositoryWithIndexAndWorkspace.Open(serverRepoPath))
        {
            File.WriteAllText(Path.Combine(serverRepoPath, "file.txt"), "v3 server");
            await serverWorkRepo.StageAsync("file.txt");
            await serverWorkRepo.CommitAsync("v3 commit");
        }

        await synchronizer.TriggerRemoteSyncAsync();

        // Assert 2: Client working tree updated with server commit
        Assert.Equal("v3 server", File.ReadAllText(Path.Combine(cloneDir, "file.txt")));
    }

    [Fact]
    public async Task CloneAsync_WithNonExistentBranch_ThrowsGitRemoteException()
    {
        CreateServerRepository("clone-fail-test", new[] { ("file.txt", "content") });
        await StartServerAsync();

        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "clone-fail");

        var ex = await Assert.ThrowsAsync<GitRemoteException>(async () =>
        {
            await GitRemoteClientRepository.CloneAsync(
                "http://localhost/clone-fail-test.git",
                cloneDir,
                options,
                branch: "nonexistent-branch");
        });

        Assert.Contains("nonexistent-branch", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_WithNonExistentBranch_ThrowsGitRemoteException()
    {
        CreateServerRepository("fetch-fail-test", new[] { ("file.txt", "content") });
        await StartServerAsync();

        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "fetch-fail");

        using var clientRepo = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/fetch-fail-test.git",
            cloneDir,
            options);

        var ex = await Assert.ThrowsAsync<GitRemoteException>(async () =>
        {
            await clientRepo.FetchAsync(branch: "does-not-exist");
        });

        Assert.Contains("does-not-exist", ex.Message);
    }

    [Fact]
    public async Task PushAsync_AlreadyUpToDate_SetsUpstreamConfig()
    {
        CreateServerRepository("push-upstream-test", new[] { ("file.txt", "content") });
        await StartServerAsync();

        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "push-upstream");

        using var clientRepo = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/push-upstream-test.git",
            cloneDir,
            options);

        // Intentionally delete upstream tracking config
        var configPath = Path.Combine(clientRepo.LocalRepository.GitDirectory, "config");
        var config = await GitConfigFile.ReadFromFileAsync(configPath, CancellationToken.None);
        config.RemoveSection("branch", "main");
        await config.WriteToFileAsync(configPath, CancellationToken.None);

        var trackingBefore = await clientRepo.GetTrackingStatusAsync("main");
        Assert.False(trackingBefore.HasUpstream);

        // Push with setUpstream when branch is already up-to-date
        await clientRepo.PushAsync(setUpstream: true);

        var trackingAfter = await clientRepo.GetTrackingStatusAsync("main");
        Assert.True(trackingAfter.HasUpstream);
        Assert.Equal("origin/main", trackingAfter.UpstreamBranch);
    }

    [Fact]
    public async Task PushAsync_NonFastForwardWithoutForce_ThrowsGitRemoteException()
    {
        CreateServerRepository("push-reject-test", new[] { ("file.txt", "base") });
        await StartServerAsync();

        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "push-reject");

        using var clientRepo = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/push-reject-test.git",
            cloneDir,
            options);

        // Advance server with a new commit
        var serverRepoPath = Path.Combine(_serverRepoRoot, "push-reject-test.git");
        using (var serverWorkRepo = GitRepositoryWithIndexAndWorkspace.Open(serverRepoPath))
        {
            File.WriteAllText(Path.Combine(serverRepoPath, "file.txt"), "server commit");
            await serverWorkRepo.StageAsync("file.txt");
            await serverWorkRepo.CommitAsync("server commit");
        }

        // Advance client independently (diverged)
        File.WriteAllText(Path.Combine(cloneDir, "file.txt"), "client commit");
        await clientRepo.WorkspaceRepository!.StageAsync("file.txt");
        await clientRepo.WorkspaceRepository!.CommitAsync("client commit");

        // Act & Assert: Push without force should throw GitRemoteException
        var ex = await Assert.ThrowsAsync<GitRemoteException>(async () =>
        {
            await clientRepo.PushAsync(force: false);
        });

        Assert.Contains("Non-fast-forward push rejected", ex.Message);
    }

    [Fact]
    public async Task PushAsync_BranchPointsToExistingCommit_SendsValidEmptyPackAndCreatesBranch()
    {
        // Arrange
        CreateServerRepository("push-empty-pack-test", new[] { ("init.txt", "init content") });
        await StartServerAsync();

        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "empty-pack-clone");

        using var clientRepo = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/push-empty-pack-test.git",
            cloneDir,
            options);

        // Create new local branch pointing to existing HEAD commit without making any new commits
        var headCommit = await clientRepo.LocalRepository.ReferenceStore.ResolveHeadAsync();
        await clientRepo.LocalRepository.ReferenceStore.CreateReferenceAsync("refs/heads/existing-ref", headCommit, overwrite: true);

        // Act: Push new branch pointing to existing commit (0 objects need to be transferred)
        await clientRepo.PushAsync(branch: "existing-ref");

        // Assert: Server now has refs/heads/existing-ref pointing to headCommit
        var serverRepoPath = Path.Combine(_serverRepoRoot, "push-empty-pack-test.git");
        var serverRepo = GitRepository.Open(serverRepoPath);
        serverRepo.InvalidateCaches();
        var serverExistingRef = await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/heads/existing-ref");

        Assert.NotNull(serverExistingRef);
        Assert.Equal(headCommit, serverExistingRef.Value);
    }

    [Fact]
    public async Task CloneAsync_WithSha256Advertisement_InitializesSha256Repository()
    {
        // Arrange
        CreateServerRepository("sha256-clone-test", new[]
        {
            ("README.md", "# SHA-256 Readme"),
            ("file.txt", "sha256 content")
        }, objectFormat: GitObjectFormat.Sha256);

        await StartServerAsync();
        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "sha256-cloned-repo");

        // Act
        using var repo = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/sha256-clone-test.git",
            cloneDir,
            options);

        // Assert
        Assert.NotNull(repo);
        Assert.Equal(GitObjectFormat.Sha256, repo.LocalRepository.ObjectFormat);
        Assert.Equal(32, repo.LocalRepository.HashLengthBytes);
        Assert.True(File.Exists(Path.Combine(cloneDir, "README.md")));
        Assert.Equal("# SHA-256 Readme", File.ReadAllText(Path.Combine(cloneDir, "README.md")));

        var trackingRef = await repo.LocalRepository.ReferenceStore.TryResolveReferenceAsync("refs/remotes/origin/main");
        var headRef = await repo.LocalRepository.ReferenceStore.TryResolveReferenceAsync("refs/heads/main");
        Assert.NotNull(trackingRef);
        Assert.NotNull(headRef);
        Assert.Equal(trackingRef, headRef);
    }

    [Fact]
    public async Task CloneAsync_WhenRemoteBranchNotFound_CleansUpTargetDirectory()
    {
        CreateServerRepository("clone-failure-test", new[]
        {
            ("README.md", "# Test Readme")
        });

        await StartServerAsync();
        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "failed-clone-repo");

        var ex = await Assert.ThrowsAsync<GitRemoteException>(async () =>
        {
            await GitRemoteClientRepository.CloneAsync(
                "http://localhost/clone-failure-test.git",
                cloneDir,
                options,
                branch: "nonexistent-branch");
        });

        Assert.Contains("nonexistent-branch", ex.Message);
        Assert.False(Directory.Exists(cloneDir));
    }

    [Fact]
    public async Task FetchAsync_WhenRemoteHasNoBranchesAndPruneIsTrue_DeletesStaleTrackingRefs()
    {
        CreateServerRepository("prune-empty-remote-test", new[]
        {
            ("file.txt", "content")
        });

        await StartServerAsync();
        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "prune-empty-clone");

        using var repo = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/prune-empty-remote-test.git",
            cloneDir,
            options);

        var trackingRefBefore = await repo.LocalRepository.ReferenceStore.TryResolveReferenceAsync("refs/remotes/origin/main");
        Assert.NotNull(trackingRefBefore);

        // Delete the server's branch so remote advertisement has 0 refs
        var serverRepoPath = Path.Combine(_serverRepoRoot, "prune-empty-remote-test.git");
        var serverRepo = GitRepository.Open(serverRepoPath);
        await serverRepo.ReferenceStore.DeleteReferenceAsync("refs/heads/main");

        // Fetch with prune: true
        await repo.FetchAsync(prune: true);

        var trackingRefAfter = await repo.LocalRepository.ReferenceStore.TryResolveReferenceAsync("refs/remotes/origin/main");
        Assert.Null(trackingRefAfter);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("feature/../main")]
    [InlineData("main/..")]
    [InlineData("bad~branch")]
    [InlineData("bad^branch")]
    [InlineData("bad:branch")]
    [InlineData("bad branch")]
    public async Task PushAsync_WithInvalidBranchName_ThrowsArgumentException(string invalidBranch)
    {
        CreateServerRepository("branch-validation-test", new[] { ("file.txt", "content") });
        await StartServerAsync();
        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "branch-val-clone");

        using var repo = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/branch-validation-test.git",
            cloneDir,
            options);

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await repo.PushAsync(branch: invalidBranch);
        });

        Assert.Equal("branch", ex.ParamName);
    }

    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("feature/../main")]
    [InlineData("bad:branch")]
    public async Task FetchAsync_WithInvalidBranchName_ThrowsArgumentException(string invalidBranch)
    {
        CreateServerRepository("fetch-branch-val-test", new[] { ("file.txt", "content") });
        await StartServerAsync();
        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };
        var cloneDir = Path.Combine(_clientWorkingDir, "fetch-branch-val-clone");

        using var repo = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/fetch-branch-val-test.git",
            cloneDir,
            options);

        var ex = await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await repo.FetchAsync(branch: invalidBranch);
        });

        Assert.Equal("branch", ex.ParamName);
    }

    [Fact]
    public async Task PushAsync_WhenObjectFormatMismatched_ThrowsGitRemoteException()
    {
        // Server repo is SHA-256
        CreateServerRepository("sha256-mismatch-server", new[] { ("file.txt", "content") }, objectFormat: GitObjectFormat.Sha256);
        await StartServerAsync();
        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };

        // Client repo is SHA-1
        var clientRepoDir = Path.Combine(_clientWorkingDir, "sha1-mismatch-client");
        var clientWs = GitRepositoryWithIndexAndWorkspace.Init(clientRepoDir, objectFormat: GitObjectFormat.Sha1);
        File.WriteAllText(Path.Combine(clientRepoDir, "file.txt"), "hello");
        await clientWs.StageAsync("file.txt");
        await clientWs.CommitAsync("Initial commit");

        using var clientRepo = new GitRemoteClientRepository(clientWs, "http://localhost/sha256-mismatch-server.git", options);

        var ex = await Assert.ThrowsAsync<GitRemoteException>(async () =>
        {
            await clientRepo.PushAsync(branch: "main");
        });

        Assert.Contains("Object format mismatch", ex.Message);
        Assert.Contains("sha1", ex.Message);
        Assert.Contains("sha256", ex.Message);
    }

    [Fact]
    public async Task FetchAsync_WhenObjectFormatMismatched_ThrowsGitRemoteException()
    {
        // Server repo is SHA-256
        CreateServerRepository("sha256-fetch-mismatch-server", new[] { ("file.txt", "content") }, objectFormat: GitObjectFormat.Sha256);
        await StartServerAsync();
        var client = _testServer!.CreateClient();
        var options = new GitRemoteClientOptions { HttpClient = client };

        // Client repo is SHA-1
        var clientRepoDir = Path.Combine(_clientWorkingDir, "sha1-fetch-mismatch-client");
        var clientWs = GitRepositoryWithIndexAndWorkspace.Init(clientRepoDir, objectFormat: GitObjectFormat.Sha1);

        using var clientRepo = new GitRemoteClientRepository(clientWs, "http://localhost/sha256-fetch-mismatch-server.git", options);

        var ex = await Assert.ThrowsAsync<GitRemoteException>(async () =>
        {
            await clientRepo.FetchAsync();
        });

        Assert.Contains("Object format mismatch", ex.Message);
    }

    public void Dispose()
    {
        _host?.Dispose();
        _testServer?.Dispose();
        TestHelper.TryDeleteDirectory(_serverRepoRoot);
        TestHelper.TryDeleteDirectory(_clientWorkingDir);
    }
}
