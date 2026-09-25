using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pmad.Git.HttpServer;
using Pmad.Git.LocalRepositories;
using Pmad.Git.RemoteClient;

namespace Pmad.Git.RemoteClient.Test;

public sealed class GitMultiClientSynchronizerEndToEndTests : IDisposable
{
    private readonly string _serverRepoRoot;
    private readonly string _clientWorkingDir;
    private IHost? _host;
    private TestServer? _testServer;

    public GitMultiClientSynchronizerEndToEndTests()
    {
        _serverRepoRoot = Path.Combine(Path.GetTempPath(), "PmadGitMultiSyncServer", Guid.NewGuid().ToString("N"));
        _clientWorkingDir = Path.Combine(Path.GetTempPath(), "PmadGitMultiSyncClients", Guid.NewGuid().ToString("N"));
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
                            options.AuthorizeAsync = static (_, _, _, _) => ValueTask.FromResult(true);
                        });
                        services.AddGitRepositorySynchronizerService();
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

    private void CreateServerRepository(string name, (string path, string content)[] files)
    {
        var barePath = Path.Combine(_serverRepoRoot, $"{name}.git");
        using var repo = GitRepositoryWithIndexAndWorkspace.Init(barePath, initialBranch: "main");

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
    public async Task ContinuousSync_CommitInRepoA_PushedAndPulledInRepoB()
    {
        // Arrange
        CreateServerRepository("continuous-sync", new[] { ("shared.txt", "v1 initial") });
        await StartServerAsync();

        var clientA = _testServer!.CreateClient();
        var clientB = _testServer!.CreateClient();

        var dirA = Path.Combine(_clientWorkingDir, "repo-a");
        var dirB = Path.Combine(_clientWorkingDir, "repo-b");

        using var clientRepoA = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/continuous-sync.git",
            dirA,
            new GitRemoteClientOptions { HttpClient = clientA });

        using var clientRepoB = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/continuous-sync.git",
            dirB,
            new GitRemoteClientOptions { HttpClient = clientB });

        var syncOptionsA = new GitRemoteClientSyncOptions
        {
            Url = "http://localhost/continuous-sync.git",
            ClientOptions = new GitRemoteClientOptions { HttpClient = clientA },
            PushDebounceDelay = TimeSpan.FromMilliseconds(50),
            PullInterval = TimeSpan.FromMilliseconds(100)
        };
        await using var synchronizerA = clientRepoA.LocalRepository.CreateSynchronizer(syncOptionsA, start: true);

        var syncOptionsB = new GitRemoteClientSyncOptions
        {
            Url = "http://localhost/continuous-sync.git",
            ClientOptions = new GitRemoteClientOptions { HttpClient = clientB },
            PushDebounceDelay = TimeSpan.FromMilliseconds(50),
            PullInterval = TimeSpan.FromMilliseconds(100)
        };
        await using var synchronizerB = clientRepoB.LocalRepository.CreateSynchronizer(syncOptionsB, start: true);

        // Act: Commit in Repo A -> debounced push to server
        var newFileInA = Path.Combine(dirA, "fromA.txt");
        await File.WriteAllTextAsync(newFileInA, "hello from repo A");
        await clientRepoA.WorkspaceRepository!.StageAsync("fromA.txt");
        var commitA = await clientRepoA.WorkspaceRepository!.CommitAsync("Commit in A");

        // Wait for debounced push to complete on the server
        var serverRepoPath = Path.Combine(_serverRepoRoot, "continuous-sync.git");
        var serverRepo = GitRepository.Open(serverRepoPath);
        var pushTimeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < pushTimeout)
        {
            serverRepo.InvalidateCaches();
            var serverHead = await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/heads/main");
            if (serverHead.HasValue && serverHead.Value == commitA)
            {
                break;
            }
            await Task.Delay(25);
        }

        serverRepo.InvalidateCaches();
        var serverRef = await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/heads/main");
        Assert.Equal(commitA, serverRef);

        // Periodic (or triggered) pull in Repo B -> verify Repo B receives Repo A's commit
        var expectedFileInB = Path.Combine(dirB, "fromA.txt");
        var pullTimeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < pullTimeout)
        {
            clientRepoB.LocalRepository.InvalidateCaches();
            var currentHead = await clientRepoB.LocalRepository.ReferenceStore.ResolveHeadAsync();
            if (currentHead == commitA && File.Exists(expectedFileInB))
            {
                break;
            }
            await Task.Delay(25);
        }

        // If periodic pull hasn't completed yet, trigger it
        if (!File.Exists(expectedFileInB))
        {
            await synchronizerB.TriggerRemoteSyncAsync();
        }

        // Assert: Repo B received Repo A's commit
        Assert.True(File.Exists(expectedFileInB));
        Assert.Equal("hello from repo A", await File.ReadAllTextAsync(expectedFileInB));

        clientRepoB.LocalRepository.InvalidateCaches();
        var headB = await clientRepoB.LocalRepository.ReferenceStore.ResolveHeadAsync();
        Assert.Equal(commitA, headB);
    }

    [Fact]
    public async Task RemoteConflict_ConcurrentCommits_EntersConflictState_ResolvesAndConverges()
    {
        // Arrange
        var initialContent = "line 1\nline 2\nline 3\n";
        CreateServerRepository("conflict-sync", new[] { ("conflict.txt", initialContent) });
        await StartServerAsync();

        var clientA = _testServer!.CreateClient();
        var clientB = _testServer!.CreateClient();

        var dirA = Path.Combine(_clientWorkingDir, "conflict-repo-a");
        var dirB = Path.Combine(_clientWorkingDir, "conflict-repo-b");

        using var clientRepoA = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/conflict-sync.git",
            dirA,
            new GitRemoteClientOptions { HttpClient = clientA });

        using var clientRepoB = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/conflict-sync.git",
            dirB,
            new GitRemoteClientOptions { HttpClient = clientB });

        var syncOptionsA = new GitRemoteClientSyncOptions
        {
            Url = "http://localhost/conflict-sync.git",
            ClientOptions = new GitRemoteClientOptions { HttpClient = clientA },
            PushDebounceDelay = TimeSpan.FromMilliseconds(50),
            PullInterval = TimeSpan.FromMilliseconds(200)
        };
        await using var synchronizerA = clientRepoA.LocalRepository.CreateSynchronizer(syncOptionsA, start: false);

        var syncOptionsB = new GitRemoteClientSyncOptions
        {
            Url = "http://localhost/conflict-sync.git",
            ClientOptions = new GitRemoteClientOptions { HttpClient = clientB },
            PushDebounceDelay = TimeSpan.FromMilliseconds(50),
            PullInterval = TimeSpan.FromMilliseconds(200)
        };
        await using var synchronizerB = clientRepoB.LocalRepository.CreateSynchronizer(syncOptionsB, start: false);

        // Step 1: Concurrent commit in Repo A
        await File.WriteAllTextAsync(Path.Combine(dirA, "conflict.txt"), "line 1 - modified by A\nline 2\nline 3\n");
        await clientRepoA.WorkspaceRepository!.StageAsync("conflict.txt");
        var commitA = await clientRepoA.WorkspaceRepository!.CommitAsync("Commit in Repo A");

        // Repo A pushes to server
        await synchronizerA.FlushPendingPushAsync();

        var serverRepoPath = Path.Combine(_serverRepoRoot, "conflict-sync.git");
        var serverRepo = GitRepository.Open(serverRepoPath);
        serverRepo.InvalidateCaches();
        var serverHead = await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/heads/main");
        Assert.Equal(commitA, serverHead);

        // Step 2: Concurrent conflicting commit in Repo B (before pulling Commit A)
        await File.WriteAllTextAsync(Path.Combine(dirB, "conflict.txt"), "line 1 - modified by B\nline 2\nline 3\n");
        await clientRepoB.WorkspaceRepository!.StageAsync("conflict.txt");
        var commitB = await clientRepoB.WorkspaceRepository!.CommitAsync("Commit in Repo B");

        // Step 3: Repo B pulls -> enters GitSyncState.Conflict
        await synchronizerB.TriggerRemoteSyncAsync();

        Assert.Equal(GitSyncState.Conflict, synchronizerB.State);
        Assert.NotNull(synchronizerB.Conflict);
        Assert.Contains("conflict.txt", synchronizerB.Conflict.ConflictedFiles);

        // Step 4: Resolve conflict in Repo B
        var resolvedContent = "line 1 - resolved by merge\nline 2\nline 3\n";
        await File.WriteAllTextAsync(Path.Combine(dirB, "conflict.txt"), resolvedContent);

        await synchronizerB.ResolveConflictAsync("conflict.txt");
        await synchronizerB.CompleteConflictResolutionAsync("Merge commit resolving conflict");

        // Verify synchronizer B resumed to Idle and pushed the merge commit
        Assert.Equal(GitSyncState.Idle, synchronizerB.State);
        Assert.Null(synchronizerB.Conflict);

        var headB = await clientRepoB.LocalRepository.ReferenceStore.ResolveHeadAsync();

        serverRepo.InvalidateCaches();
        var serverHeadAfterMerge = await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/heads/main");
        Assert.Equal(headB, serverHeadAfterMerge);

        // Step 5: Repo A pulls the merge commit
        await synchronizerA.TriggerRemoteSyncAsync();

        // Step 6: Verify both repositories converge
        var headA = await clientRepoA.LocalRepository.ReferenceStore.ResolveHeadAsync();
        Assert.Equal(headB, headA);

        Assert.Equal(resolvedContent, await File.ReadAllTextAsync(Path.Combine(dirA, "conflict.txt")));
        Assert.Equal(resolvedContent, await File.ReadAllTextAsync(Path.Combine(dirB, "conflict.txt")));
        Assert.Equal(GitSyncState.Idle, synchronizerA.State);
        Assert.Equal(GitSyncState.Idle, synchronizerB.State);
    }

    [Fact]
    public async Task ConcurrentCommits_AutoReconciles_CleanlyMergesAndPushesWithoutStalling()
    {
        // Arrange
        var initialContent = "shared content\n";
        CreateServerRepository("autoreconcile-sync", new[] { ("shared.txt", initialContent) });
        await StartServerAsync();

        var clientA = _testServer!.CreateClient();
        var clientB = _testServer!.CreateClient();

        var dirA = Path.Combine(_clientWorkingDir, "reconcile-repo-a");
        var dirB = Path.Combine(_clientWorkingDir, "reconcile-repo-b");

        using var clientRepoA = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/autoreconcile-sync.git",
            dirA,
            new GitRemoteClientOptions { HttpClient = clientA });

        using var clientRepoB = await GitRemoteClientRepository.CloneAsync(
            "http://localhost/autoreconcile-sync.git",
            dirB,
            new GitRemoteClientOptions { HttpClient = clientB });

        var syncOptionsA = new GitRemoteClientSyncOptions
        {
            Url = "http://localhost/autoreconcile-sync.git",
            ClientOptions = new GitRemoteClientOptions { HttpClient = clientA },
            PushDebounceDelay = TimeSpan.FromMilliseconds(50),
            PullInterval = TimeSpan.FromMilliseconds(200)
        };
        await using var synchronizerA = clientRepoA.LocalRepository.CreateSynchronizer(syncOptionsA, start: false);

        var syncOptionsB = new GitRemoteClientSyncOptions
        {
            Url = "http://localhost/autoreconcile-sync.git",
            ClientOptions = new GitRemoteClientOptions { HttpClient = clientB },
            PushDebounceDelay = TimeSpan.FromMilliseconds(50),
            PullInterval = TimeSpan.FromMilliseconds(200)
        };
        await using var synchronizerB = clientRepoB.LocalRepository.CreateSynchronizer(syncOptionsB, start: false);

        // Step 1: Commit and push in Repo A
        await File.WriteAllTextAsync(Path.Combine(dirA, "fileA.txt"), "hello from A\n");
        await clientRepoA.WorkspaceRepository!.StageAsync("fileA.txt");
        var commitA = await clientRepoA.WorkspaceRepository!.CommitAsync("Commit in Repo A");

        synchronizerA.NotifyLocalChange();
        await synchronizerA.FlushPendingPushAsync();

        var serverRepoPath = Path.Combine(_serverRepoRoot, "autoreconcile-sync.git");
        var serverRepo = GitRepository.Open(serverRepoPath);
        serverRepo.InvalidateCaches();
        var serverHead = await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/heads/main");
        Assert.Equal(commitA, serverHead);

        // Step 2: Concurrent non-conflicting commit in Repo B (before pulling Commit A)
        await File.WriteAllTextAsync(Path.Combine(dirB, "fileB.txt"), "hello from B\n");
        await clientRepoB.WorkspaceRepository!.StageAsync("fileB.txt");
        var commitB = await clientRepoB.WorkspaceRepository!.CommitAsync("Commit in Repo B");

        // Step 3: Repo B flushes pending push -> non-fast-forward rejection -> auto-reconcile (pulls A, 3-way merges, retries push)
        synchronizerB.NotifyLocalChange();
        await synchronizerB.FlushPendingPushAsync();

        // Verify synchronizer B successfully auto-reconciled, cleanly merged, and pushed to server
        Assert.Equal(GitSyncState.Idle, synchronizerB.State);
        Assert.Null(synchronizerB.Conflict);

        var headB = await clientRepoB.LocalRepository.ReferenceStore.ResolveHeadAsync();
        Assert.NotEqual(GitHash.Zero, headB);
        Assert.NotEqual(commitA, headB);
        Assert.NotEqual(commitB, headB);

        serverRepo.InvalidateCaches();
        var serverHeadAfterReconcile = await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/heads/main");
        Assert.Equal(headB, serverHeadAfterReconcile);

        // Both files exist in Repo B
        Assert.True(File.Exists(Path.Combine(dirB, "fileA.txt")));
        Assert.True(File.Exists(Path.Combine(dirB, "fileB.txt")));
        Assert.Equal("hello from A\n", await File.ReadAllTextAsync(Path.Combine(dirB, "fileA.txt")));
        Assert.Equal("hello from B\n", await File.ReadAllTextAsync(Path.Combine(dirB, "fileB.txt")));

        // Step 4: Repo A pulls the merge commit
        await synchronizerA.TriggerRemoteSyncAsync();

        var headA = await clientRepoA.LocalRepository.ReferenceStore.ResolveHeadAsync();
        Assert.Equal(headB, headA);

        // Both files exist in Repo A
        Assert.True(File.Exists(Path.Combine(dirA, "fileA.txt")));
        Assert.True(File.Exists(Path.Combine(dirA, "fileB.txt")));
        Assert.Equal("hello from A\n", await File.ReadAllTextAsync(Path.Combine(dirA, "fileA.txt")));
        Assert.Equal("hello from B\n", await File.ReadAllTextAsync(Path.Combine(dirA, "fileB.txt")));
        Assert.Equal(GitSyncState.Idle, synchronizerA.State);
    }

    [Fact]
    public async Task HttpServer_SynchronizerService_SetupSynchronizer_WithRemoteClient_CachesAndSyncs()
    {
        CreateServerRepository("service-sync", new[] { ("file.txt", "v1") });
        await StartServerAsync();

        var repoPath = Path.Combine(_serverRepoRoot, "service-sync.git");
        var syncService = _host!.Services.GetRequiredService<IGitRepositorySynchronizerService>();

        var synchronizer = syncService.SetupSynchronizer(repoPath, repo => repo.CreateSynchronizer(new GitRemoteClientSyncOptions
        {
            Url = "http://localhost/service-sync.git",
            ClientOptions = new GitRemoteClientOptions { HttpClient = _testServer!.CreateClient() },
            PushDebounceDelay = TimeSpan.FromMilliseconds(50),
            PullInterval = TimeSpan.FromMilliseconds(100)
        }, start: false));

        Assert.NotNull(synchronizer);
        Assert.Same(synchronizer, syncService.GetSynchronizerByPath(repoPath));
    }

    public void Dispose()
    {
        _host?.Dispose();
        _testServer?.Dispose();
        TestHelper.TryDeleteDirectory(_serverRepoRoot);
        TestHelper.TryDeleteDirectory(_clientWorkingDir);
    }
}

