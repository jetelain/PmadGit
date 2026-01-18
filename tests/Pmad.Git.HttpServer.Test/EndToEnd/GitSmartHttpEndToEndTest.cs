using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
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
            try { Directory.Delete(tempWorkDir, true); } catch { }
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
            
            File.WriteAllText(Path.Combine(tempWorkDir, "new-file.txt"), "new content");
            RunGit(tempWorkDir, "add new-file.txt");
            RunGit(tempWorkDir, "commit -m \"New commit\" --quiet");
            RunGit(tempWorkDir, "push origin main --quiet");
        }
        finally
        {
            try { Directory.Delete(tempWorkDir, true); } catch { }
        }

        // Act: Fetch
        var fetchOutput = RunGit(cloneDir, "fetch origin");

        // Assert: Verify new commits are fetched
        var logOutput = RunGit(cloneDir, "log origin/main --oneline");
        Assert.Contains("New commit", logOutput);
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
            
            File.WriteAllText(Path.Combine(tempWorkDir, "pulled-file.txt"), "pulled content");
            RunGit(tempWorkDir, "add pulled-file.txt");
            RunGit(tempWorkDir, "commit -m \"Commit to pull\" --quiet");
            RunGit(tempWorkDir, "push origin main --quiet");
        }
        finally
        {
            try { Directory.Delete(tempWorkDir, true); } catch { }
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
            try { Directory.Delete(tempWorkDir, true); } catch { }
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
            try { Directory.Delete(tempWorkDir, true); } catch { }
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
            try { Directory.Delete(workDir, true); } catch { }
        }
        
        return GitRepository.Open(bareRepoPath);
    }

    private async Task StartServerAsync(bool enableUploadPack = true, bool enableReceivePack = false, string routePrefix = "git")
    {
        var builder = WebApplication.CreateBuilder();
        
        // Configure to listen on a random available port
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        // Enable synchronous IO for compatibility with git clients
        builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
        {
            options.AllowSynchronousIO = true;
        });

        var app = builder.Build();

        var options = new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            RoutePrefix = routePrefix,
            EnableUploadPack = enableUploadPack,
            EnableReceivePack = enableReceivePack
        };

        app.MapGitSmartHttp(options);

        _host = app;
        await _host.StartAsync();

        // Get the actual port that was assigned
        var addresses = app.Urls;
        _serverUrl = addresses.First() + $"/{routePrefix}";
    }

    private string RunGit(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start git process");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"git {arguments} failed with exit code {process.ExitCode}:{Environment.NewLine}{error}{Environment.NewLine}{output}");
        }

        return string.IsNullOrEmpty(output) ? error : output;
    }

    public void Dispose()
    {
        _host?.StopAsync().GetAwaiter().GetResult();
        _host?.Dispose();
        
        try { Directory.Delete(_serverRepoRoot, true); } catch { }
        try { Directory.Delete(_clientWorkingDir, true); } catch { }
    }
}
