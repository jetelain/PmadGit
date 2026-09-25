using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pmad.Git.Cli;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Test.EndToEnd;

public sealed class GitCliSmartHttpEndToEndTest : IDisposable
{
    private readonly string _serverRepoRoot;
    private readonly string _clientWorkingDir;
    private IHost? _host;
    private string? _serverUrl;

    public GitCliSmartHttpEndToEndTest()
    {
        _serverRepoRoot = Path.Combine(Path.GetTempPath(), "PmadGitCliHttpServerE2E", Guid.NewGuid().ToString("N"));
        _clientWorkingDir = Path.Combine(Path.GetTempPath(), "PmadGitCliHttpClientE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_serverRepoRoot);
        Directory.CreateDirectory(_clientWorkingDir);
    }

    [Fact]
    public async Task GitCli_CloneAsync_OverSmartHttp_Succeeds()
    {
        // Arrange
        CreateSourceRepository("cli-clone-repo", new[]
        {
            ("README.md", "# Cloned Repo"),
            ("docs/guide.txt", "Guide details")
        });

        await StartServerAsync(enableUploadPack: true, enableReceivePack: true);

        var cloneDir = Path.Combine(_clientWorkingDir, "cloned-via-cli");
        var remoteUrl = $"{_serverUrl}/cli-clone-repo.git";

        // Act
        var cliRepo = await GitCliRepository.CloneAsync(remoteUrl, cloneDir);

        // Assert
        Assert.NotNull(cliRepo);
        Assert.True(Directory.Exists(cloneDir));
        Assert.True(File.Exists(Path.Combine(cloneDir, "README.md")));
        Assert.Equal("# Cloned Repo", File.ReadAllText(Path.Combine(cloneDir, "README.md")));

        var branch = await cliRepo.GetCurrentBranchAsync();
        Assert.Equal("main", branch);

        var tracking = await cliRepo.GetTrackingStatusAsync();
        Assert.Equal("main", tracking.LocalBranch);
        Assert.Equal("origin/main", tracking.UpstreamBranch);
        Assert.Equal(0, tracking.AheadCount);
        Assert.Equal(0, tracking.BehindCount);
    }

    [Fact]
    public async Task GitCli_CloneAsync_SpecificBranch_OverSmartHttp_Succeeds()
    {
        // Arrange
        var bareRepo = CreateSourceRepository("branch-repo", new[] { ("file.txt", "main content") });
        var barePath = Path.Combine(_serverRepoRoot, "branch-repo.git");

        // Create a feature branch on the server repo
        var tempWork = Path.Combine(Path.GetTempPath(), "temp-work-branch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWork);
        try
        {
            TestHelper.RunGit(tempWork, $"clone --quiet \"{barePath}\" .");
            TestHelper.RunGit(tempWork, "config user.name \"Test User\"");
            TestHelper.RunGit(tempWork, "config user.email test@example.com");
            TestHelper.RunGit(tempWork, "checkout -b feature-test");
            File.WriteAllText(Path.Combine(tempWork, "feature.txt"), "feature content");
            TestHelper.RunGit(tempWork, "add feature.txt");
            TestHelper.RunGit(tempWork, "commit -m \"Feature commit\" --quiet");
            TestHelper.RunGit(tempWork, "push origin feature-test --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempWork);
        }

        await StartServerAsync(enableUploadPack: true, enableReceivePack: true);

        var cloneDir = Path.Combine(_clientWorkingDir, "cloned-feature-branch");
        var remoteUrl = $"{_serverUrl}/branch-repo.git";

        // Act
        var cliRepo = await GitCliRepository.CloneAsync(remoteUrl, cloneDir, branch: "feature-test");

        // Assert
        Assert.NotNull(cliRepo);
        Assert.Equal("feature-test", await cliRepo.GetCurrentBranchAsync());
        Assert.True(File.Exists(Path.Combine(cloneDir, "feature.txt")));
        Assert.Equal("feature content", File.ReadAllText(Path.Combine(cloneDir, "feature.txt")));
    }

    [Fact]
    public async Task GitCli_PushAndPullAsync_OverSmartHttp_SyncsCommitsSuccessfully()
    {
        // Arrange
        CreateSourceRepository("push-pull-repo", new[] { ("initial.txt", "initial") });
        await StartServerAsync(enableUploadPack: true, enableReceivePack: true);

        var remoteUrl = $"{_serverUrl}/push-pull-repo.git";

        // Clone two client repositories via GitCliRepository
        var client1Dir = Path.Combine(_clientWorkingDir, "client1");
        var client2Dir = Path.Combine(_clientWorkingDir, "client2");

        var client1 = await GitCliRepository.CloneAsync(remoteUrl, client1Dir);
        var client2 = await GitCliRepository.CloneAsync(remoteUrl, client2Dir);

        await ConfigureUserAsync(client1);
        await ConfigureUserAsync(client2);

        // Act 1: Client 1 creates a commit and pushes to server over HTTP
        File.WriteAllText(Path.Combine(client1Dir, "from-client1.txt"), "hello from client 1");
        TestHelper.RunGit(client1Dir, "add from-client1.txt");
        await client1.CommitAsync("Commit from client 1");

        var trackingBeforePush = await client1.GetTrackingStatusAsync();
        Assert.Equal(1, trackingBeforePush.AheadCount);
        Assert.Equal(0, trackingBeforePush.BehindCount);

        await client1.PushAsync(setUpstream: true);

        var trackingAfterPush = await client1.GetTrackingStatusAsync();
        Assert.Equal(0, trackingAfterPush.AheadCount);
        Assert.Equal(0, trackingAfterPush.BehindCount);

        // Act 2: Client 2 fetches and pulls the changes over HTTP
        await client2.FetchAsync();
        var client2Tracking = await client2.GetTrackingStatusAsync();
        Assert.Equal(0, client2Tracking.AheadCount);
        Assert.Equal(1, client2Tracking.BehindCount);

        var pullResult = await client2.PullAsync();

        // Assert
        Assert.True(pullResult.IsSuccess);
        Assert.Empty(pullResult.ConflictedFiles);
        Assert.True(File.Exists(Path.Combine(client2Dir, "from-client1.txt")));
        Assert.Equal("hello from client 1", File.ReadAllText(Path.Combine(client2Dir, "from-client1.txt")));
    }

    [Fact]
    public async Task GitCli_PullAsync_WithConflicts_ReportsConflictedFiles()
    {
        // Arrange
        CreateSourceRepository("conflict-repo", new[] { ("shared.txt", "line 1\nline 2\n") });
        await StartServerAsync(enableUploadPack: true, enableReceivePack: true);

        var remoteUrl = $"{_serverUrl}/conflict-repo.git";

        var client1Dir = Path.Combine(_clientWorkingDir, "conflict-client1");
        var client2Dir = Path.Combine(_clientWorkingDir, "conflict-client2");

        var client1 = await GitCliRepository.CloneAsync(remoteUrl, client1Dir);
        var client2 = await GitCliRepository.CloneAsync(remoteUrl, client2Dir);

        await ConfigureUserAsync(client1);
        await ConfigureUserAsync(client2);

        // Client 1 modifies shared.txt and pushes
        File.WriteAllText(Path.Combine(client1Dir, "shared.txt"), "line 1 client 1\nline 2\n");
        TestHelper.RunGit(client1Dir, "add shared.txt");
        await client1.CommitAsync("Client 1 update");
        await client1.PushAsync();

        // Client 2 modifies same line in shared.txt and commits locally
        File.WriteAllText(Path.Combine(client2Dir, "shared.txt"), "line 1 client 2\nline 2\n");
        TestHelper.RunGit(client2Dir, "add shared.txt");
        await client2.CommitAsync("Client 2 update");

        // Act: Client 2 pulls over HTTP, causing a merge conflict
        var pullResult = await client2.PullAsync();

        // Assert
        Assert.False(pullResult.IsSuccess);
        Assert.Contains("shared.txt", pullResult.ConflictedFiles);
        Assert.True(await client2.IsMergeInProgressAsync());

        // Resolve conflict
        File.WriteAllText(Path.Combine(client2Dir, "shared.txt"), "line 1 resolved\nline 2\n");
        await client2.ResolveConflictAsync("shared.txt");
        await client2.ContinueMergeAsync("Resolved conflict");

        Assert.False(await client2.IsMergeInProgressAsync());

        // Now push resolved merge to server
        await client2.PushAsync();

        // Client 1 pulls resolved merge
        var client1Pull = await client1.PullAsync();
        Assert.True(client1Pull.IsSuccess);
        Assert.Equal("line 1 resolved\nline 2\n", File.ReadAllText(Path.Combine(client1Dir, "shared.txt")).Replace("\r\n", "\n"));
    }

    [Fact]
    public async Task GitCli_PushNewBranchAndFetchWithPrune_OverSmartHttp_Succeeds()
    {
        // Arrange
        CreateSourceRepository("prune-repo", new[] { ("readme.txt", "hello") });
        await StartServerAsync(enableUploadPack: true, enableReceivePack: true);

        var remoteUrl = $"{_serverUrl}/prune-repo.git";
        var clientDir = Path.Combine(_clientWorkingDir, "prune-client");
        var client = await GitCliRepository.CloneAsync(remoteUrl, clientDir);
        await ConfigureUserAsync(client);
        // Explicitly set fetch.prune to false so the test is isolated from user's global git config
        await client.SetConfigAsync("fetch.prune", "false");

        // Act 1: Create local branch and push it to server over HTTP
        await client.CreateBranchAsync("topic-alpha");
        await client.CheckoutAsync("topic-alpha");
        File.WriteAllText(Path.Combine(clientDir, "alpha.txt"), "topic content");
        TestHelper.RunGit(clientDir, "add alpha.txt");
        await client.CommitAsync("Topic commit");
        await client.PushAsync("origin", "topic-alpha", setUpstream: true);

        var branchesWithRemote = await client.GetBranchesAsync(includeRemote: true);
        Assert.Contains("origin/topic-alpha", branchesWithRemote);

        // Act 2: Delete branch directly on server repo
        var barePath = Path.Combine(_serverRepoRoot, "prune-repo.git");
        TestHelper.RunGit(barePath, "branch -D topic-alpha");

        // Fetch without prune: remote tracking still exists
        await client.FetchAsync();
        var branchesBeforePrune = await client.GetBranchesAsync(includeRemote: true);
        Assert.Contains("origin/topic-alpha", branchesBeforePrune);

        // Fetch with prune over HTTP: remote tracking ref is removed
        await client.FetchAsync(prune: true);
        var branchesAfterPrune = await client.GetBranchesAsync(includeRemote: true);
        Assert.DoesNotContain("origin/topic-alpha", branchesAfterPrune);
    }

    [Fact]
    public async Task GitCli_ForcePush_OverSmartHttp_OverwritesRemoteBranch()
    {
        // Arrange
        CreateSourceRepository("force-repo", new[] { ("file.txt", "v1") });
        await StartServerAsync(enableUploadPack: true, enableReceivePack: true);

        var remoteUrl = $"{_serverUrl}/force-repo.git";
        var clientDir = Path.Combine(_clientWorkingDir, "force-client");
        var client = await GitCliRepository.CloneAsync(remoteUrl, clientDir);
        await ConfigureUserAsync(client);

        // Commit v2 and push
        File.WriteAllText(Path.Combine(clientDir, "file.txt"), "v2");
        TestHelper.RunGit(clientDir, "add file.txt");
        await client.CommitAsync("v2 commit");
        await client.PushAsync();

        // Amend commit (rewriting history)
        File.WriteAllText(Path.Combine(clientDir, "file.txt"), "v2 rewritten");
        TestHelper.RunGit(clientDir, "add file.txt");
        await client.CommitAmendAsync("v2 amended");

        // Normal push fails due to non-fast-forward
        await Assert.ThrowsAnyAsync<Exception>(() => client.PushAsync());

        // Act: Force push succeeds
        await client.PushAsync(force: true);

        // Assert: New clone from server has the amended content
        var verifyDir = Path.Combine(_clientWorkingDir, "verify-force");
        var verifyClient = await GitCliRepository.CloneAsync(remoteUrl, verifyDir);
        Assert.Equal("v2 rewritten", File.ReadAllText(Path.Combine(verifyDir, "file.txt")));
    }

    private static async Task ConfigureUserAsync(GitCliRepository repo)
    {
        await repo.SetConfigAsync("user.name", "Test User");
        await repo.SetConfigAsync("user.email", "test@example.com");
    }

    private GitRepository CreateSourceRepository(string name, (string path, string content)[] files)
    {
        var bareRepoPath = Path.Combine(_serverRepoRoot, $"{name}.git");
        Directory.CreateDirectory(bareRepoPath);

        TestHelper.RunGit(bareRepoPath, "init --bare --quiet --initial-branch=main");

        var workDir = Path.Combine(Path.GetTempPath(), "temp-source", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            TestHelper.RunGit(workDir, "init --quiet --initial-branch=main");
            TestHelper.RunGit(workDir, "config user.name \"Test User\"");
            TestHelper.RunGit(workDir, "config user.email test@example.com");

            foreach (var (path, content) in files)
            {
                var fullPath = Path.Combine(workDir, path.Replace('/', Path.DirectorySeparatorChar));
                var dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(fullPath, content);
            }

            TestHelper.RunGit(workDir, "add -A");
            TestHelper.RunGit(workDir, "commit -m \"Initial commit\" --quiet");
            TestHelper.RunGit(workDir, $"remote add origin \"{bareRepoPath}\"");
            TestHelper.RunGit(workDir, "push -u origin main --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(workDir);
        }

        return GitRepository.Open(bareRepoPath);
    }

    private async Task StartServerAsync(
        bool enableUploadPack = true,
        bool enableReceivePack = false,
        string routePrefix = "git")
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddGitSmartHttp(options =>
        {
            options.RepositoryRoot = _serverRepoRoot;
            options.EnableUploadPack = enableUploadPack;
            options.EnableReceivePack = enableReceivePack;
            if (enableReceivePack)
            {
                options.AuthorizeAsync = (_, _, _, _) => ValueTask.FromResult(true);
            }
        });

        var app = builder.Build();
        app.MapGitSmartHttp("/" + routePrefix + "/{*repository}.git");

        _host = app;
        await _host.StartAsync();

        var addresses = app.Urls;
        _serverUrl = addresses.First() + $"/{routePrefix}";
    }

    public void Dispose()
    {
        TestHelper.SafeStop(_host);
        TestHelper.TryDeleteDirectory(_serverRepoRoot);
        TestHelper.TryDeleteDirectory(_clientWorkingDir);
    }
}
