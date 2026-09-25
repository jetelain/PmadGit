using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Test.EndToEnd;

public sealed class GitSmartHttpEndToEndTest : IDisposable
{
    private readonly string _serverRepoRoot;
    private readonly string _clientWorkingDir;
    private IHost? _host;
    private string? _serverUrl;

    public GitSmartHttpEndToEndTest()
    {
        _serverRepoRoot = Path.Combine(Path.GetTempPath(), "PmadGitHttpServerE2E", Guid.NewGuid().ToString("N"));
        _clientWorkingDir = Path.Combine(Path.GetTempPath(), "PmadGitHttpClientE2E", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_serverRepoRoot);
        Directory.CreateDirectory(_clientWorkingDir);
    }

    [Fact]
    public async Task GitClone_WithSimpleRepository_ShouldSucceed()
    {
        // Arrange: Create a repository with some content
        var sourceRepo = CreateSourceRepository("test-repo", new[]
        {
            ("README.md", "# Test Repository"),
            ("src/file.txt", "test content")
        });

        await StartServerAsync();

        // Act: Clone with git CLI
        var cloneDir = Path.Combine(_clientWorkingDir, "cloned-repo");
        var output = RunGit(_clientWorkingDir, $"clone {_serverUrl}/test-repo.git {cloneDir}");

        // Assert: Verify clone succeeded
        Assert.True(Directory.Exists(cloneDir));
        Assert.True(File.Exists(Path.Combine(cloneDir, "README.md")));
        Assert.True(File.Exists(Path.Combine(cloneDir, "src", "file.txt")));

        var readmeContent = File.ReadAllText(Path.Combine(cloneDir, "README.md"));
        Assert.Equal("# Test Repository", readmeContent);
    }

    [Fact]
    public async Task GitClone_WithMultipleCommits_ShouldCloneCompleteHistory()
    {
        // Arrange: Create repository with multiple commits
        var repoPath = Path.Combine(_serverRepoRoot, "multi-commit.git");
        Directory.CreateDirectory(repoPath);

        RunGit(repoPath, "init --bare --quiet --initial-branch=main");

        var tempWorkDir = Path.Combine(Path.GetTempPath(), "temp-work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkDir);

        try
        {
            RunGit(tempWorkDir, "init --quiet --initial-branch=main");
            RunGit(tempWorkDir, "config user.name \"Test\"");
            RunGit(tempWorkDir, "config user.email test@test.com");

            File.WriteAllText(Path.Combine(tempWorkDir, "file1.txt"), "content 1");
            RunGit(tempWorkDir, "add file1.txt");
            RunGit(tempWorkDir, "commit -m \"First commit\" --quiet");

            File.WriteAllText(Path.Combine(tempWorkDir, "file2.txt"), "content 2");
            RunGit(tempWorkDir, "add file2.txt");
            RunGit(tempWorkDir, "commit -m \"Second commit\" --quiet");

            File.WriteAllText(Path.Combine(tempWorkDir, "file3.txt"), "content 3");
            RunGit(tempWorkDir, "add file3.txt");
            RunGit(tempWorkDir, "commit -m \"Third commit\" --quiet");

            RunGit(tempWorkDir, $"remote add origin \"{repoPath}\"");
            RunGit(tempWorkDir, "push -u origin main --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempWorkDir);
        }

        await StartServerAsync();

        // Act: Clone
        var cloneDir = Path.Combine(_clientWorkingDir, "multi-commit-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/multi-commit.git {cloneDir}");

        // Assert: Verify all commits are present
        var logOutput = RunGit(cloneDir, "log --oneline");
        Assert.Contains("Third commit", logOutput);
        Assert.Contains("Second commit", logOutput);
        Assert.Contains("First commit", logOutput);

        Assert.True(File.Exists(Path.Combine(cloneDir, "file1.txt")));
        Assert.True(File.Exists(Path.Combine(cloneDir, "file2.txt")));
        Assert.True(File.Exists(Path.Combine(cloneDir, "file3.txt")));
    }

    [Fact]
    public async Task GitFetch_AfterNewCommits_ShouldRetrieveNewCommits()
    {
        // Arrange: Create initial repository
        var sourceRepo = CreateSourceRepository("fetch-test", new[] { ("initial.txt", "initial") });
        await StartServerAsync();

        var cloneDir = Path.Combine(_clientWorkingDir, "fetch-test-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/fetch-test.git {cloneDir}");

        // Add new commits to bare repository via a temp working directory
        var tempWorkDir = Path.Combine(Path.GetTempPath(), "temp-fetch-update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkDir);
        try
        {
            var bareRepoPath = Path.Combine(_serverRepoRoot, "fetch-test.git");
            RunGit(tempWorkDir, $"clone \"{bareRepoPath}\" .");
            RunGit(tempWorkDir, "config user.name \"Test\"");
            RunGit(tempWorkDir, "config user.email test@test.com");

            File.WriteAllText(Path.Combine(tempWorkDir, "new-file.txt"), "new content");
            RunGit(tempWorkDir, "add new-file.txt");
            RunGit(tempWorkDir, "commit -m \"New commit\" --quiet");
            RunGit(tempWorkDir, "push origin main --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempWorkDir);
        }

        // Act: Fetch
        var fetchOutput = RunGit(cloneDir, "fetch origin");

        // Assert: Verify new commits are fetched
        var logOutput = RunGit(cloneDir, "log origin/main --oneline");
        Assert.Contains("New commit", logOutput);
    }

    [Fact]
    public async Task GitFetch_IncrementalFetch_OnlyTransfersIncrementalObjects()
    {
        // Arrange: Create initial repository with commit 1
        var sourceRepo = CreateSourceRepository("incremental-fetch-test", new[] { ("initial.txt", "initial content") });
        await StartServerAsync();

        var cloneDir = Path.Combine(_clientWorkingDir, "incremental-fetch-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/incremental-fetch-test.git {cloneDir}");

        // Add commit 2 to the bare repository
        var tempWorkDir = Path.Combine(Path.GetTempPath(), "temp-incremental-fetch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkDir);
        try
        {
            var bareRepoPath = Path.Combine(_serverRepoRoot, "incremental-fetch-test.git");
            RunGit(tempWorkDir, $"clone \"{bareRepoPath}\" .");
            RunGit(tempWorkDir, "config user.name \"Test\"");
            RunGit(tempWorkDir, "config user.email test@test.com");

            File.WriteAllText(Path.Combine(tempWorkDir, "new-file.txt"), "new content");
            RunGit(tempWorkDir, "add new-file.txt");
            RunGit(tempWorkDir, "commit -m \"Second commit\" --quiet");
            RunGit(tempWorkDir, "push origin main --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempWorkDir);
        }

        // Before fetch, get pack files in clone
        var packDir = Path.Combine(cloneDir, ".git", "objects", "pack");
        var existingPacks = Directory.Exists(packDir) ? Directory.GetFiles(packDir, "*.pack") : Array.Empty<string>();

        // Act: Fetch
        RunGit(cloneDir, "fetch origin");

        // Assert: New commit is fetched
        var logOutput = RunGit(cloneDir, "log origin/main --oneline");
        Assert.Contains("Second commit", logOutput);

        // Find newly downloaded packfile
        var currentPacks = Directory.GetFiles(packDir, "*.pack");
        var newPacks = currentPacks.Except(existingPacks).ToList();
        if (newPacks.Count > 0)
        {
            // Verify pack object count using git verify-pack:
            // An incremental pack for just 1 new file should only contain 3 objects (commit, tree, blob),
            // not the initial commit/tree/blob.
            var verifyOutput = RunGit(cloneDir, $"verify-pack -v \"{newPacks[0]}\"");
            var objectLines = verifyOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("commit") || line.Contains("tree") || line.Contains("blob"))
                .ToList();
            Assert.Equal(3, objectLines.Count);
        }
    }

    [Fact]
    public async Task GitClone_HierarchicalRepository_WithAndWithoutGitSuffix()
    {
        // Arrange: Create a hierarchical repository under org/team/project.git
        var repoDir = Path.Combine(_serverRepoRoot, "org", "team", "project.git");
        Directory.CreateDirectory(repoDir);
        RunGit(repoDir, "init --bare --quiet --initial-branch=main");

        var tempWorkDir = Path.Combine(Path.GetTempPath(), "temp-hierarchical-work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkDir);
        try
        {
            RunGit(tempWorkDir, "init --quiet --initial-branch=main");
            RunGit(tempWorkDir, "config user.name \"Test\"");
            RunGit(tempWorkDir, "config user.email test@test.com");
            File.WriteAllText(Path.Combine(tempWorkDir, "nested.txt"), "hierarchical content");
            RunGit(tempWorkDir, "add nested.txt");
            RunGit(tempWorkDir, "commit -m \"Initial commit\" --quiet");
            RunGit(tempWorkDir, $"remote add origin \"{repoDir}\"");
            RunGit(tempWorkDir, "push -u origin main --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempWorkDir);
        }

        await StartServerAsync();

        // Act 1: Clone with .git suffix
        var cloneDirWithGit = Path.Combine(_clientWorkingDir, "hierarchical-with-git");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/org/team/project.git {cloneDirWithGit}");
        Assert.True(File.Exists(Path.Combine(cloneDirWithGit, "nested.txt")));

        // Act 2: Clone without .git suffix
        var cloneDirWithoutGit = Path.Combine(_clientWorkingDir, "hierarchical-without-git");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/org/team/project {cloneDirWithoutGit}");
        Assert.True(File.Exists(Path.Combine(cloneDirWithoutGit, "nested.txt")));
    }

    [Fact]
    public async Task GitPull_AfterNewCommits_ShouldMergeNewCommits()
    {
        // Arrange: Create initial repository
        var sourceRepo = CreateSourceRepository("pull-test", new[] { ("initial.txt", "initial") });
        await StartServerAsync();

        var cloneDir = Path.Combine(_clientWorkingDir, "pull-test-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/pull-test.git {cloneDir}");

        // Add new commits to bare repository via a temp working directory
        var tempWorkDir = Path.Combine(Path.GetTempPath(), "temp-pull-update", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkDir);
        try
        {
            var bareRepoPath = Path.Combine(_serverRepoRoot, "pull-test.git");
            RunGit(tempWorkDir, $"clone \"{bareRepoPath}\" .");
            RunGit(tempWorkDir, "config user.name \"Test\"");
            RunGit(tempWorkDir, "config user.email test@test.com");

            File.WriteAllText(Path.Combine(tempWorkDir, "pulled-file.txt"), "pulled content");
            RunGit(tempWorkDir, "add pulled-file.txt");
            RunGit(tempWorkDir, "commit -m \"Commit to pull\" --quiet");
            RunGit(tempWorkDir, "push origin main --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempWorkDir);
        }

        // Act: Pull
        RunGit(cloneDir, "pull origin main");

        // Assert: Verify file from new commit is present
        Assert.True(File.Exists(Path.Combine(cloneDir, "pulled-file.txt")));
        var content = File.ReadAllText(Path.Combine(cloneDir, "pulled-file.txt"));
        Assert.Equal("pulled content", content);
    }

    [Fact]
    public async Task GitPush_WithNewCommits_ShouldUploadToServer()
    {
        // Arrange: Create initial repository and enable receive-pack
        var sourceRepo = CreateSourceRepository("push-test", new[] { ("initial.txt", "initial") });

        // Start server with push enabled
        await StartServerAsync(enableReceivePack: true);

        var cloneDir = Path.Combine(_clientWorkingDir, "push-test-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/push-test.git {cloneDir}");
        RunGit(cloneDir, "config user.name \"Test\"");
        RunGit(cloneDir, "config user.email test@test.com");

        // Create new commit in clone
        File.WriteAllText(Path.Combine(cloneDir, "pushed-file.txt"), "pushed content");
        RunGit(cloneDir, "add pushed-file.txt");
        RunGit(cloneDir, "commit -m \"Commit to push\" --quiet");

        // Act: Push
        var pushOutput = RunGit(cloneDir, "push origin main");

        // Assert: Verify push succeeded
        Assert.Contains("main -> main", pushOutput);

        // Verify file is in server repository
        var bareRepoPath = Path.Combine(_serverRepoRoot, "push-test.git");
        var verifyWorkDir = Path.Combine(_clientWorkingDir, "verify-push");
        RunGit(_clientWorkingDir, $"clone \"{bareRepoPath}\" {verifyWorkDir}");
        Assert.True(File.Exists(Path.Combine(verifyWorkDir, "pushed-file.txt")));
    }

    [Fact]
    public async Task GitClone_WithLargeFiles_ShouldSucceed()
    {
        // Arrange: Create repository with large file
        var largeContent = new string('A', 1024 * 1024); // 1MB
        var sourceRepo = CreateSourceRepository("large-file-test", new[]
        {
            ("large.txt", largeContent),
            ("small.txt", "small")
        });

        await StartServerAsync();

        // Act: Clone
        var cloneDir = Path.Combine(_clientWorkingDir, "large-file-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/large-file-test.git {cloneDir}");

        // Assert
        Assert.True(File.Exists(Path.Combine(cloneDir, "large.txt")));
        var clonedContent = File.ReadAllText(Path.Combine(cloneDir, "large.txt"));
        Assert.Equal(largeContent.Length, clonedContent.Length);
    }

    [Fact]
    public async Task GitClone_WithNestedDirectories_ShouldPreserveStructure()
    {
        // Arrange
        var sourceRepo = CreateSourceRepository("nested-test", new[]
        {
            ("a/b/c/deep.txt", "deep file"),
            ("a/b/mid.txt", "mid file"),
            ("a/top.txt", "top file"),
            ("root.txt", "root file")
        });

        await StartServerAsync();

        // Act
        var cloneDir = Path.Combine(_clientWorkingDir, "nested-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/nested-test.git {cloneDir}");

        // Assert
        Assert.True(File.Exists(Path.Combine(cloneDir, "a", "b", "c", "deep.txt")));
        Assert.True(File.Exists(Path.Combine(cloneDir, "a", "b", "mid.txt")));
        Assert.True(File.Exists(Path.Combine(cloneDir, "a", "top.txt")));
        Assert.True(File.Exists(Path.Combine(cloneDir, "root.txt")));
    }

    [Fact]
    public async Task GitClone_WithBranches_ShouldCloneAllBranches()
    {
        // Arrange: Create repository with multiple branches
        var repoPath = Path.Combine(_serverRepoRoot, "branches-test.git");
        Directory.CreateDirectory(repoPath);

        RunGit(repoPath, "init --bare --quiet --initial-branch=main");

        var tempWorkDir = Path.Combine(Path.GetTempPath(), "temp-branches", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkDir);

        try
        {
            RunGit(tempWorkDir, "init --quiet --initial-branch=main");
            RunGit(tempWorkDir, "config user.name \"Test\"");
            RunGit(tempWorkDir, "config user.email test@test.com");

            File.WriteAllText(Path.Combine(tempWorkDir, "main.txt"), "main branch");
            RunGit(tempWorkDir, "add main.txt");
            RunGit(tempWorkDir, "commit -m \"Main commit\" --quiet");

            RunGit(tempWorkDir, "checkout -b feature --quiet");
            File.WriteAllText(Path.Combine(tempWorkDir, "feature.txt"), "feature branch");
            RunGit(tempWorkDir, "add feature.txt");
            RunGit(tempWorkDir, "commit -m \"Feature commit\" --quiet");

            RunGit(tempWorkDir, "checkout main --quiet");
            RunGit(tempWorkDir, $"remote add origin \"{repoPath}\"");
            RunGit(tempWorkDir, "push -u origin --all --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempWorkDir);
        }

        await StartServerAsync();

        // Act
        var cloneDir = Path.Combine(_clientWorkingDir, "branches-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/branches-test.git {cloneDir}");

        // Assert
        var branchOutput = RunGit(cloneDir, "branch -r");
        Assert.Contains("origin/main", branchOutput);
        Assert.Contains("origin/feature", branchOutput);
    }

    [Fact]
    public async Task GitClone_WithTags_ShouldCloneTags()
    {
        // Arrange: Create repository with tags
        var repoPath = Path.Combine(_serverRepoRoot, "tags-test.git");
        Directory.CreateDirectory(repoPath);

        RunGit(repoPath, "init --bare --quiet --initial-branch=main");

        var tempWorkDir = Path.Combine(Path.GetTempPath(), "temp-tags", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkDir);

        try
        {
            RunGit(tempWorkDir, "init --quiet --initial-branch=main");
            RunGit(tempWorkDir, "config user.name \"Test\"");
            RunGit(tempWorkDir, "config user.email test@test.com");

            File.WriteAllText(Path.Combine(tempWorkDir, "v1.txt"), "version 1");
            RunGit(tempWorkDir, "add v1.txt");
            RunGit(tempWorkDir, "commit -m \"Version 1\" --quiet");
            RunGit(tempWorkDir, "tag -a v1.0 -m \"Version 1.0\"");

            File.WriteAllText(Path.Combine(tempWorkDir, "v2.txt"), "version 2");
            RunGit(tempWorkDir, "add v2.txt");
            RunGit(tempWorkDir, "commit -m \"Version 2\" --quiet");
            RunGit(tempWorkDir, "tag -a v2.0 -m \"Version 2.0\"");

            RunGit(tempWorkDir, $"remote add origin \"{repoPath}\"");
            RunGit(tempWorkDir, "push -u origin --all --quiet");
            RunGit(tempWorkDir, "push origin --tags --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempWorkDir);
        }

        await StartServerAsync();

        // Act
        var cloneDir = Path.Combine(_clientWorkingDir, "tags-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/tags-test.git {cloneDir}");

        // Assert
        var tagOutput = RunGit(cloneDir, "tag");
        Assert.Contains("v1.0", tagOutput);
        Assert.Contains("v2.0", tagOutput);
    }

    [Fact]
    public async Task GitClone_WithDisabledUploadPack_ShouldFail()
    {
        // Arrange
        var sourceRepo = CreateSourceRepository("disabled-upload", new[] { ("file.txt", "content") });
        await StartServerAsync(enableUploadPack: false);

        // Act & Assert
        var cloneDir = Path.Combine(_clientWorkingDir, "disabled-clone");
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RunGit(_clientWorkingDir, $"clone {_serverUrl}/disabled-upload.git {cloneDir}"));

        Assert.Contains("exit code", exception.Message);
    }

    [Fact]
    public async Task GitClone_WithCustomRoutePrefix_ShouldSucceed()
    {
        // Arrange
        var sourceRepo = CreateSourceRepository("custom-prefix", new[] { ("file.txt", "content") });
        await StartServerAsync(routePrefix: "my-git");

        // Act
        var serverUrlWithPrefix = _serverUrl!.Replace("/git", "/my-git");
        var cloneDir = Path.Combine(_clientWorkingDir, "custom-prefix-clone");
        RunGit(_clientWorkingDir, $"clone {serverUrlWithPrefix}/custom-prefix.git {cloneDir}");

        // Assert
        Assert.True(File.Exists(Path.Combine(cloneDir, "file.txt")));
    }

    [Fact]
    public async Task GitPush_WithSuccessfulUpdates_ShouldInvokeCallback()
    {
        // Arrange: Setup callback tracking
        var callbackInvoked = false;
        string? capturedRepositoryName = null;
        IReadOnlyList<string>? capturedUpdatedRefs = null;
        var callbackCompletionSource = new TaskCompletionSource<bool>();

        var sourceRepo = CreateSourceRepository("callback-test", new[] { ("initial.txt", "initial") });

        // Start server with push enabled and callback
        await StartServerAsync(enableReceivePack: true, onReceivePackCompleted: (ctx, repoName, updatedRefs) =>
        {
            callbackInvoked = true;
            capturedRepositoryName = repoName;
            capturedUpdatedRefs = updatedRefs;
            callbackCompletionSource.SetResult(true);
            return ValueTask.CompletedTask;
        });

        var cloneDir = Path.Combine(_clientWorkingDir, "callback-test-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/callback-test.git {cloneDir}");
        RunGit(cloneDir, "config user.name \"Test\"");
        RunGit(cloneDir, "config user.email test@test.com");

        // Create new commit in clone
        File.WriteAllText(Path.Combine(cloneDir, "pushed-file.txt"), "pushed content");
        RunGit(cloneDir, "add pushed-file.txt");
        RunGit(cloneDir, "commit -m \"Trigger callback\" --quiet");

        // Act: Push
        RunGit(cloneDir, "push origin main");

        // Wait for callback to complete (with timeout)
        var completed = await Task.WhenAny(callbackCompletionSource.Task, Task.Delay(5000));
        Assert.Same(callbackCompletionSource.Task, completed);

        // Assert: Verify callback was invoked with correct parameters
        Assert.True(callbackInvoked);
        Assert.Equal("callback-test", capturedRepositoryName);
        Assert.NotNull(capturedUpdatedRefs);
        Assert.Single(capturedUpdatedRefs);
        Assert.Contains("refs/heads/main", capturedUpdatedRefs);
    }

    [Fact]
    public async Task GitPush_WithCallbackException_ShouldNotFailPush()
    {
        // Arrange: Setup callback that throws
        var callbackInvoked = false;
        var callbackCompletionSource = new TaskCompletionSource<bool>();

        var sourceRepo = CreateSourceRepository("exception-test", new[] { ("initial.txt", "initial") });

        // Start server with push enabled and throwing callback
        await StartServerAsync(enableReceivePack: true, onReceivePackCompleted: (ctx, repoName, updatedRefs) =>
        {
            callbackInvoked = true;
            callbackCompletionSource.SetResult(true);
            throw new InvalidOperationException("Callback failed!");
        });

        var cloneDir = Path.Combine(_clientWorkingDir, "exception-test-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/exception-test.git {cloneDir}");
        RunGit(cloneDir, "config user.name \"Test\"");
        RunGit(cloneDir, "config user.email test@test.com");

        // Create new commit
        File.WriteAllText(Path.Combine(cloneDir, "file.txt"), "content");
        RunGit(cloneDir, "add file.txt");
        RunGit(cloneDir, "commit -m \"Test commit\" --quiet");

        // Act: Push (should succeed despite callback exception)
        var pushOutput = RunGit(cloneDir, "push origin main");

        // Wait for callback to be invoked
        var completed = await Task.WhenAny(callbackCompletionSource.Task, Task.Delay(5000));
        Assert.Same(callbackCompletionSource.Task, completed);

        // Assert: Push succeeded and callback was invoked
        Assert.Contains("main -> main", pushOutput);
        Assert.True(callbackInvoked);
    }

    [Fact]
    public async Task GitPush_WithSlowCallback_ShouldNotBlockResponse()
    {
        // Arrange: Setup slow callback
        var callbackStarted = new TaskCompletionSource<bool>();
        var callbackCanFinish = new TaskCompletionSource<bool>();
        var callbackFinished = new TaskCompletionSource<bool>();

        var sourceRepo = CreateSourceRepository("slow-callback-test", new[] { ("initial.txt", "initial") });

        // Start server with push enabled and slow callback
        await StartServerAsync(enableReceivePack: true, onReceivePackCompleted: async (ctx, repoName, updatedRefs) =>
        {
            callbackStarted.SetResult(true);
            await callbackCanFinish.Task;
            callbackFinished.SetResult(true);
        });

        var cloneDir = Path.Combine(_clientWorkingDir, "slow-callback-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/slow-callback-test.git {cloneDir}");
        RunGit(cloneDir, "config user.name \"Test\"");
        RunGit(cloneDir, "config user.email test@test.com");

        // Create new commit
        File.WriteAllText(Path.Combine(cloneDir, "file.txt"), "content");
        RunGit(cloneDir, "add file.txt");
        RunGit(cloneDir, "commit -m \"Test commit\" --quiet");

        // Act: Push (should complete while callback is still running)
        var pushTask = Task.Run(() => RunGit(cloneDir, "push origin main"));

        // Wait for callback to start
        var callbackStartedCompleted = await Task.WhenAny(callbackStarted.Task, Task.Delay(5000));
        Assert.Same(callbackStarted.Task, callbackStartedCompleted);

        // Push should complete even though callback is blocked
        var pushOutput = await pushTask;
        Assert.Contains("main -> main", pushOutput);

        // Verify callback hasn't finished yet (proving it didn't block the push)
        Assert.False(callbackFinished.Task.IsCompleted);

        // Clean up - allow callback to complete
        callbackCanFinish.SetResult(true);

        // Verify callback eventually completes
        var callbackFinishedCompleted = await Task.WhenAny(callbackFinished.Task, Task.Delay(5000));
        Assert.Same(callbackFinished.Task, callbackFinishedCompleted);
    }

    [Fact]
    public async Task GitPush_WithMultipleBranches_ShouldInvokeCallbackWithAllRefs()
    {
        // Arrange: Setup callback tracking
        var callbackInvoked = false;
        IReadOnlyList<string>? capturedUpdatedRefs = null;
        var callbackCompletionSource = new TaskCompletionSource<bool>();

        var sourceRepo = CreateSourceRepository("multi-branch-test", new[] { ("initial.txt", "initial") });

        // Start server with callback
        await StartServerAsync(enableReceivePack: true, onReceivePackCompleted: (ctx, repoName, updatedRefs) =>
        {
            callbackInvoked = true;
            capturedUpdatedRefs = updatedRefs;
            callbackCompletionSource.SetResult(true);
            return ValueTask.CompletedTask;
        });

        var cloneDir = Path.Combine(_clientWorkingDir, "multi-branch-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/multi-branch-test.git {cloneDir}");
        RunGit(cloneDir, "config user.name \"Test\"");
        RunGit(cloneDir, "config user.email test@test.com");

        // Create new branches
        RunGit(cloneDir, "checkout -b feature1 --quiet");
        File.WriteAllText(Path.Combine(cloneDir, "feature1.txt"), "feature 1");
        RunGit(cloneDir, "add feature1.txt");
        RunGit(cloneDir, "commit -m \"Feature 1\" --quiet");

        RunGit(cloneDir, "checkout -b feature2 main --quiet");
        File.WriteAllText(Path.Combine(cloneDir, "feature2.txt"), "feature 2");
        RunGit(cloneDir, "add feature2.txt");
        RunGit(cloneDir, "commit -m \"Feature 2\" --quiet");

        // Act: Push all branches
        RunGit(cloneDir, "push origin feature1 feature2");

        // Wait for callback
        var completed = await Task.WhenAny(callbackCompletionSource.Task, Task.Delay(5000));
        Assert.Same(callbackCompletionSource.Task, completed);

        // Assert: Callback received both branch updates
        Assert.True(callbackInvoked);
        Assert.NotNull(capturedUpdatedRefs);
        Assert.Equal(2, capturedUpdatedRefs.Count);
        Assert.Contains("refs/heads/feature1", capturedUpdatedRefs);
        Assert.Contains("refs/heads/feature2", capturedUpdatedRefs);
    }

    [Fact]
    public async Task GitPush_ShouldRaiseChangedOnceWithUpToDateState()
    {
        // Arrange: verify that a real push raises IGitRepositoryCacheInvalidator.Changed exactly
        // once, and that by the time it is raised the repository already reflects the new ref
        // value (not a stale one), so a debounced synchronizer observing Changed would push the
        // correct, final state.
        var sourceRepo = CreateSourceRepository("changed-event-test", new[] { ("initial.txt", "initial") });
        await StartServerAsync(enableReceivePack: true);

        var repositoryService = _host!.Services.GetRequiredService<IGitRepositoryService>();
        var repository = repositoryService.GetRepositoryByPath(Path.Combine(_serverRepoRoot, "changed-event-test.git"));
        var changedCount = 0;
        GitHash? mainRefAtLastChanged = null;
        repository.Changed += (_, _) =>
        {
            changedCount++;
#pragma warning disable xUnit1031 // Synchronous event handler must capture state at the moment Changed is raised
            mainRefAtLastChanged = repository.ReferenceStore.TryResolveReferenceAsync("refs/heads/main").GetAwaiter().GetResult();
#pragma warning restore xUnit1031
        };

        var cloneDir = Path.Combine(_clientWorkingDir, "changed-event-test-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/changed-event-test.git {cloneDir}");
        RunGit(cloneDir, "config user.name \"Test\"");
        RunGit(cloneDir, "config user.email test@test.com");

        File.WriteAllText(Path.Combine(cloneDir, "pushed-file.txt"), "pushed content");
        RunGit(cloneDir, "add pushed-file.txt");
        RunGit(cloneDir, "commit -m \"Trigger changed\" --quiet");

        // Act: Push
        RunGit(cloneDir, "push origin main");
        var expectedHash = RunGit(cloneDir, "rev-parse HEAD").Trim();

        // Assert: Changed raised exactly once, and its associated ref value already matches
        // the new commit (not the previous/stale one).
        Assert.Equal(1, changedCount);
        Assert.NotNull(mainRefAtLastChanged);
        Assert.Equal(expectedHash, mainRefAtLastChanged.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitClone_AndPush_EmptyRepository_ShouldSucceed()
    {
        // Arrange: create an empty bare repository on the server
        var bareRepoPath = Path.Combine(_serverRepoRoot, "empty-repo.git");
        Directory.CreateDirectory(bareRepoPath);
        RunGit(bareRepoPath, "init --bare --quiet --initial-branch=main");

        await StartServerAsync(enableUploadPack: true, enableReceivePack: true);

        // Act 1: Clone the empty repository
        var cloneDir = Path.Combine(_clientWorkingDir, "cloned-empty");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/empty-repo.git {cloneDir}");
        Assert.True(Directory.Exists(cloneDir));

        // Act 2: Create initial commit in the clone and push to the server
        RunGit(cloneDir, "config user.name \"Test User\"");
        RunGit(cloneDir, "config user.email test@example.com");
        File.WriteAllText(Path.Combine(cloneDir, "README.md"), "# Empty Repo Initialized");
        RunGit(cloneDir, "add README.md");
        RunGit(cloneDir, "commit -m \"Initial commit\" --quiet");
        RunGit(cloneDir, "branch -M main");
        var pushOutput = RunGit(cloneDir, "push -u origin main");

        // Assert: Push succeeded
        Assert.True(pushOutput.Contains("main") || pushOutput.Contains("set up to track"));

        // Verify content exists on server by cloning into a fresh directory
        var verifyDir = Path.Combine(_clientWorkingDir, "verify-empty-cloned");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/empty-repo.git {verifyDir}");
        Assert.True(File.Exists(Path.Combine(verifyDir, "README.md")));
        Assert.Equal("# Empty Repo Initialized", File.ReadAllText(Path.Combine(verifyDir, "README.md")));
    }

    [Fact]
    public async Task GitFetch_Sha256Repository_ShouldSucceed()
    {
        // Arrange: create a SHA-256 bare repository on the server
        var bareRepoPath = Path.Combine(_serverRepoRoot, "sha256-repo.git");
        Directory.CreateDirectory(bareRepoPath);
        RunGit(bareRepoPath, "init --bare --quiet --object-format=sha256 --initial-branch=main");

        // Populate it with a commit
        var workDir = Path.Combine(Path.GetTempPath(), "temp-sha256-source", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            RunGit(workDir, "init --quiet --object-format=sha256 --initial-branch=main");
            RunGit(workDir, "config user.name \"Test User\"");
            RunGit(workDir, "config user.email test@example.com");
            File.WriteAllText(Path.Combine(workDir, "sha256.txt"), "sha256 content");
            RunGit(workDir, "add sha256.txt");
            RunGit(workDir, "commit -m \"SHA-256 commit\" --quiet");
            RunGit(workDir, $"remote add origin \"{bareRepoPath}\"");
            RunGit(workDir, "push -u origin main --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(workDir);
        }

        await StartServerAsync(enableUploadPack: true);

        // Act: In Git protocol v0/v1, client repository must be initialized with SHA-256 before fetch
        var clientDir = Path.Combine(_clientWorkingDir, "fetched-sha256");
        Directory.CreateDirectory(clientDir);
        RunGit(clientDir, "init --quiet --object-format=sha256 --initial-branch=main");
        RunGit(clientDir, $"remote add origin {_serverUrl}/sha256-repo.git");
        RunGit(clientDir, "fetch origin main");
        RunGit(clientDir, "checkout -b main origin/main --quiet");

        // Assert
        Assert.True(File.Exists(Path.Combine(clientDir, "sha256.txt")));
        Assert.Equal("sha256 content", File.ReadAllText(Path.Combine(clientDir, "sha256.txt")));

        var format = RunGit(clientDir, "rev-parse --show-object-format").Trim();
        Assert.Equal("sha256", format);
    }

    [Fact]
    public async Task GitPush_DeleteBranch_ShouldDeleteOnServerAndNotifyCallback()
    {
        // Arrange: Create a repository with main and feature branches
        var repoPath = Path.Combine(_serverRepoRoot, "delete-branch-test.git");
        Directory.CreateDirectory(repoPath);
        RunGit(repoPath, "init --bare --quiet --initial-branch=main");

        var tempWorkDir = Path.Combine(Path.GetTempPath(), "temp-del-branch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkDir);
        try
        {
            RunGit(tempWorkDir, "init --quiet --initial-branch=main");
            RunGit(tempWorkDir, "config user.name \"Test\"");
            RunGit(tempWorkDir, "config user.email test@test.com");
            File.WriteAllText(Path.Combine(tempWorkDir, "main.txt"), "main content");
            RunGit(tempWorkDir, "add main.txt");
            RunGit(tempWorkDir, "commit -m \"Main commit\" --quiet");
            RunGit(tempWorkDir, "checkout -b to-delete --quiet");
            File.WriteAllText(Path.Combine(tempWorkDir, "del.txt"), "to be deleted");
            RunGit(tempWorkDir, "add del.txt");
            RunGit(tempWorkDir, "commit -m \"Branch commit\" --quiet");
            RunGit(tempWorkDir, $"remote add origin \"{repoPath}\"");
            RunGit(tempWorkDir, "push -u origin --all --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempWorkDir);
        }

        var callbackInvoked = false;
        IReadOnlyList<string>? callbackUpdatedRefs = null;
        var callbackTcs = new TaskCompletionSource<bool>();

        await StartServerAsync(enableReceivePack: true, onReceivePackCompleted: (ctx, repoName, updatedRefs) =>
        {
            callbackInvoked = true;
            callbackUpdatedRefs = updatedRefs;
            callbackTcs.SetResult(true);
            return ValueTask.CompletedTask;
        });

        var cloneDir = Path.Combine(_clientWorkingDir, "delete-branch-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/delete-branch-test.git {cloneDir}");

        // Act: Delete branch on remote using git push origin --delete <branch>
        RunGit(cloneDir, "push origin --delete to-delete");

        // Wait for callback
        var completed = await Task.WhenAny(callbackTcs.Task, Task.Delay(5000));
        Assert.Same(callbackTcs.Task, completed);

        // Assert: Branch deleted on server
        var serverRepo = GitRepository.Open(repoPath);
        serverRepo.InvalidateCaches(raiseChanged: false);
        var refVal = await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/heads/to-delete");
        Assert.Null(refVal);

        // Assert: Callback notified with deleted ref
        Assert.True(callbackInvoked);
        Assert.NotNull(callbackUpdatedRefs);
        Assert.Contains("refs/heads/to-delete", callbackUpdatedRefs);

        // Client git branch -r does not have origin/to-delete
        var remoteBranches = RunGit(cloneDir, "branch -r");
        Assert.DoesNotContain("origin/to-delete", remoteBranches);
    }

    [Fact]
    public async Task GitPush_DeleteTag_ShouldDeleteTagOnServer()
    {
        var sourceRepo = CreateSourceRepository("delete-tag-test", new[] { ("file.txt", "v1") });
        var serverRepoPath = Path.Combine(_serverRepoRoot, "delete-tag-test.git");
        var headCommit = await sourceRepo.ReferenceStore.ResolveHeadAsync();
        await sourceRepo.ReferenceStore.CreateReferenceAsync("refs/tags/v1.0", headCommit);

        await StartServerAsync(enableReceivePack: true);

        var cloneDir = Path.Combine(_clientWorkingDir, "delete-tag-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/delete-tag-test.git {cloneDir}");
        var tagsBefore = RunGit(cloneDir, "tag -l");
        Assert.Contains("v1.0", tagsBefore);

        // Act: Delete tag on remote using git push origin --delete <tag>
        RunGit(cloneDir, "push origin --delete v1.0");

        // Assert: Tag is deleted on server
        sourceRepo.InvalidateCaches(raiseChanged: false);
        var tagRef = await sourceRepo.ReferenceStore.TryResolveReferenceAsync("refs/tags/v1.0");
        Assert.Null(tagRef);

        // Verify fresh clone has no tags
        var freshCloneDir = Path.Combine(_clientWorkingDir, "delete-tag-fresh-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/delete-tag-test.git {freshCloneDir}");
        var freshTags = RunGit(freshCloneDir, "tag -l").Trim();
        Assert.Empty(freshTags);
    }

    [Fact]
    public async Task GitPush_AnnotatedAndLightweightTags_ShouldUploadTagObjectsAndRefs()
    {
        var sourceRepo = CreateSourceRepository("push-tags-test", new[] { ("file.txt", "initial") });
        await StartServerAsync(enableReceivePack: true);

        var cloneDir = Path.Combine(_clientWorkingDir, "push-tags-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/push-tags-test.git {cloneDir}");
        RunGit(cloneDir, "config user.name \"Test User\"");
        RunGit(cloneDir, "config user.email test@example.com");

        // Create lightweight tag
        RunGit(cloneDir, "tag v1.0-light");

        // Create annotated tag
        RunGit(cloneDir, "tag -a v1.0-annotated -m \"Annotated release tag message\"");

        // Act: Push tags to server
        var pushOutput = RunGit(cloneDir, "push origin --tags");
        Assert.Contains("v1.0-light", pushOutput);
        Assert.Contains("v1.0-annotated", pushOutput);

        // Assert on server: Both tags exist
        var serverRepoPath = Path.Combine(_serverRepoRoot, "push-tags-test.git");
        var serverRepo = GitRepository.Open(serverRepoPath);
        serverRepo.InvalidateCaches(raiseChanged: false);
        var lightRef = await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/tags/v1.0-light");
        var annotatedRef = await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/tags/v1.0-annotated");

        Assert.NotNull(lightRef);
        Assert.NotNull(annotatedRef);

        // Annotated tag ref points to a Tag object, not commit object directly
        var tagObj = await serverRepo.ObjectStore.ReadObjectAsync(annotatedRef.Value);
        Assert.Equal(GitObjectType.Tag, tagObj.Type);

        // Clone into another directory and verify native git sees tag annotation
        var verifyCloneDir = Path.Combine(_clientWorkingDir, "push-tags-verify-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/push-tags-test.git {verifyCloneDir}");
        var catType = RunGit(verifyCloneDir, "cat-file -t v1.0-annotated").Trim();
        Assert.Equal("tag", catType);

        var tagDetails = RunGit(verifyCloneDir, "tag -n9 -l v1.0-annotated");
        Assert.Contains("Annotated release tag message", tagDetails);
    }

    [Fact]
    public async Task GitPush_ForcePushAfterReset_ShouldUpdateRefOnServer()
    {
        var sourceRepo = CreateSourceRepository("force-push-test", new[] { ("file.txt", "v1") });
        await StartServerAsync(enableReceivePack: true);

        var cloneDir = Path.Combine(_clientWorkingDir, "force-push-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/force-push-test.git {cloneDir}");
        RunGit(cloneDir, "config user.name \"Test User\"");
        RunGit(cloneDir, "config user.email test@example.com");

        // Add commit 2 and push
        File.WriteAllText(Path.Combine(cloneDir, "file.txt"), "v2");
        RunGit(cloneDir, "add file.txt");
        RunGit(cloneDir, "commit -m \"Commit 2\" --quiet");
        RunGit(cloneDir, "push origin main");

        // Reset back to commit 1 and make alternate commit 3 (diverging)
        RunGit(cloneDir, "reset --hard HEAD~1");
        File.WriteAllText(Path.Combine(cloneDir, "alt.txt"), "divergent content");
        RunGit(cloneDir, "add alt.txt");
        RunGit(cloneDir, "commit -m \"Commit 3 alternate\" --quiet");

        // Act & Assert 1: Normal push must be rejected (non-fast-forward)
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RunGit(cloneDir, "push origin main"));
        Assert.True(
            ex.Message.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("rejected", StringComparison.OrdinalIgnoreCase));

        // Act 2: Force push must succeed
        var forceOutput = RunGit(cloneDir, "push --force origin main");
        Assert.Contains("forced update", forceOutput);

        // Assert on server: main points to the alternate commit
        var serverRepoPath = Path.Combine(_serverRepoRoot, "force-push-test.git");
        var serverRepo = GitRepository.Open(serverRepoPath);
        serverRepo.InvalidateCaches(raiseChanged: false);
        var expectedHash = RunGit(cloneDir, "rev-parse HEAD").Trim();
        var serverMain = await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/heads/main");
        Assert.Equal(expectedHash, serverMain.ToString(), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GitPush_MixedBatchOperations_UpdatesCreatesAndDeletesAtomic()
    {
        // Arrange
        var repoPath = Path.Combine(_serverRepoRoot, "mixed-batch-test.git");
        Directory.CreateDirectory(repoPath);
        RunGit(repoPath, "init --bare --quiet --initial-branch=main");

        var tempWorkDir = Path.Combine(Path.GetTempPath(), "temp-mixed-batch", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempWorkDir);
        try
        {
            RunGit(tempWorkDir, "init --quiet --initial-branch=main");
            RunGit(tempWorkDir, "config user.name \"Test\"");
            RunGit(tempWorkDir, "config user.email test@test.com");
            File.WriteAllText(Path.Combine(tempWorkDir, "main.txt"), "main content");
            RunGit(tempWorkDir, "add main.txt");
            RunGit(tempWorkDir, "commit -m \"Initial\" --quiet");
            RunGit(tempWorkDir, "checkout -b to-delete --quiet");
            File.WriteAllText(Path.Combine(tempWorkDir, "del.txt"), "to be deleted");
            RunGit(tempWorkDir, "add del.txt");
            RunGit(tempWorkDir, "commit -m \"To delete\" --quiet");
            RunGit(tempWorkDir, $"remote add origin \"{repoPath}\"");
            RunGit(tempWorkDir, "push -u origin --all --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(tempWorkDir);
        }

        var callbackTcs = new TaskCompletionSource<bool>();
        IReadOnlyList<string>? updatedRefsCaptured = null;
        await StartServerAsync(enableReceivePack: true, onReceivePackCompleted: (ctx, repoName, updatedRefs) =>
        {
            updatedRefsCaptured = updatedRefs;
            callbackTcs.SetResult(true);
            return ValueTask.CompletedTask;
        });

        var cloneDir = Path.Combine(_clientWorkingDir, "mixed-batch-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/mixed-batch-test.git {cloneDir}");
        RunGit(cloneDir, "config user.name \"Test\"");
        RunGit(cloneDir, "config user.email test@test.com");

        // Advance main
        File.WriteAllText(Path.Combine(cloneDir, "main2.txt"), "main updated");
        RunGit(cloneDir, "add main2.txt");
        RunGit(cloneDir, "commit -m \"Advance main\" --quiet");

        // Create new branch
        RunGit(cloneDir, "checkout -b to-create --quiet");
        File.WriteAllText(Path.Combine(cloneDir, "create.txt"), "created");
        RunGit(cloneDir, "add create.txt");
        RunGit(cloneDir, "commit -m \"New branch\" --quiet");

        // Act: Push advance main, create to-create, delete to-delete in single push
        RunGit(cloneDir, "push origin main to-create :to-delete");

        var completed = await Task.WhenAny(callbackTcs.Task, Task.Delay(5000));
        Assert.Same(callbackTcs.Task, completed);

        // Assert
        var serverRepo = GitRepository.Open(repoPath);
        serverRepo.InvalidateCaches(raiseChanged: false);
        Assert.NotNull(await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/heads/main"));
        Assert.NotNull(await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/heads/to-create"));
        Assert.Null(await serverRepo.ReferenceStore.TryResolveReferenceAsync("refs/heads/to-delete"));

        Assert.NotNull(updatedRefsCaptured);
        Assert.Contains("refs/heads/main", updatedRefsCaptured);
        Assert.Contains("refs/heads/to-create", updatedRefsCaptured);
        Assert.Contains("refs/heads/to-delete", updatedRefsCaptured);
    }

    [Fact]
    public async Task GitClone_DetachedHeadRepository_ShouldSucceed()
    {
        var sourceRepo = CreateSourceRepository("detached-head-test", new[] { ("file.txt", "detached content") });
        var headCommit = await sourceRepo.ReferenceStore.ResolveHeadAsync();

        // Put the bare server repo into detached HEAD state by storing commit hash in HEAD
        var bareHeadPath = Path.Combine(_serverRepoRoot, "detached-head-test.git", "HEAD");
        await File.WriteAllTextAsync(bareHeadPath, headCommit.ToString() + "\n");

        await StartServerAsync(enableUploadPack: true);

        // Act: Clone repository with detached HEAD
        var cloneDir = Path.Combine(_clientWorkingDir, "detached-clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/detached-head-test.git {cloneDir}");

        // Assert: Clone succeeded and content is available
        Assert.True(File.Exists(Path.Combine(cloneDir, "file.txt")));
        Assert.Equal("detached content", File.ReadAllText(Path.Combine(cloneDir, "file.txt")));
    }

    private GitRepository CreateSourceRepository(string name, (string path, string content)[] files)
    {
        var bareRepoPath = Path.Combine(_serverRepoRoot, $"{name}.git");
        Directory.CreateDirectory(bareRepoPath);

        RunGit(bareRepoPath, "init --bare --quiet --initial-branch=main");

        var workDir = Path.Combine(Path.GetTempPath(), "temp-source", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);

        try
        {
            RunGit(workDir, "init --quiet --initial-branch=main");
            RunGit(workDir, "config user.name \"Test User\"");
            RunGit(workDir, "config user.email test@example.com");

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

            RunGit(workDir, "add -A");
            RunGit(workDir, "commit -m \"Initial commit\" --quiet");
            RunGit(workDir, $"remote add origin \"{bareRepoPath}\"");
            RunGit(workDir, "push -u origin main --quiet");
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
        string routePrefix = "git",
        Func<HttpContext, string, IReadOnlyList<string>, ValueTask>? onReceivePackCompleted = null)
    {
        var builder = WebApplication.CreateBuilder();

        // Configure to listen on a random available port
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Register Git Smart HTTP service
        builder.Services.AddGitSmartHttp(options =>
        {
            options.RepositoryRoot = _serverRepoRoot;
            options.EnableUploadPack = enableUploadPack;
            options.EnableReceivePack = enableReceivePack;

            // For testing purposes, allow both read and write operations
            if (enableReceivePack)
            {
                options.AuthorizeAsync = (_, _, _, _) => ValueTask.FromResult(true);
            }

            // Set callback if provided
            if (onReceivePackCompleted != null)
            {
                options.OnReceivePackCompleted = onReceivePackCompleted;
            }
        });

        var app = builder.Build();

        // Map Git Smart HTTP endpoints
        app.MapGitSmartHttp("/" + routePrefix + "/{*repository}.git");

        _host = app;
        await _host.StartAsync();

        // Get the actual port that was assigned
        var addresses = app.Urls;
        _serverUrl = addresses.First() + $"/{routePrefix}";
    }

    private static string RunGit(string workingDirectory, string arguments)
    {
        return TestHelper.RunGit(workingDirectory, arguments);
    }

    public void Dispose()
    {
        TestHelper.SafeStop(_host);
        TestHelper.TryDeleteDirectory(_serverRepoRoot);
        TestHelper.TryDeleteDirectory(_clientWorkingDir);
    }
}
