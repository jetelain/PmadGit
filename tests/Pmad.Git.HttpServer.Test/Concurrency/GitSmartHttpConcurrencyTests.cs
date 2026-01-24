using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Test.Concurrency;

/// <summary>
/// Tests for concurrency protection in push operations via GitSmartHttpService.
/// </summary>
public sealed class GitSmartHttpConcurrencyTests : IDisposable
{
    private readonly string _serverRepoRoot;
    private readonly string _clientWorkingDir;
    private IHost? _host;
    private string? _serverUrl;

    public GitSmartHttpConcurrencyTests()
    {
        _serverRepoRoot = Path.Combine(Path.GetTempPath(), "PmadGitHttpConcurrencyTest", Guid.NewGuid().ToString("N"));
        _clientWorkingDir = Path.Combine(Path.GetTempPath(), "PmadGitHttpConcurrencyClient", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_serverRepoRoot);
        Directory.CreateDirectory(_clientWorkingDir);
    }

    [Fact]
    public async Task ConcurrentPushes_AreSerialized()
    {
        // Arrange
        CreateSourceRepository("concurrent-test", new[] { ("initial.txt", "initial") });
        await StartServerAsync(enableReceivePack: true);

        // Create two separate clones
        var clone1Dir = Path.Combine(_clientWorkingDir, "clone1");
        var clone2Dir = Path.Combine(_clientWorkingDir, "clone2");
        
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/concurrent-test.git {clone1Dir}");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/concurrent-test.git {clone2Dir}");

        ConfigureGit(clone1Dir);
        ConfigureGit(clone2Dir);

        // Create commits in both clones
        File.WriteAllText(Path.Combine(clone1Dir, "file1.txt"), "from clone1");
        RunGit(clone1Dir, "add file1.txt");
        RunGit(clone1Dir, "commit -m \"Commit from clone1\" --quiet");

        File.WriteAllText(Path.Combine(clone2Dir, "file2.txt"), "from clone2");
        RunGit(clone2Dir, "add file2.txt");
        RunGit(clone2Dir, "commit -m \"Commit from clone2\" --quiet");

        var results = new System.Collections.Concurrent.ConcurrentBag<(int clone, bool success, string output)>();

        // Act - Push from both clones concurrently
        var push1 = Task.Run(() =>
        {
            try
            {
                var output = RunGit(clone1Dir, "push origin main");
                results.Add((1, true, output));
            }
            catch (Exception ex)
            {
                results.Add((1, false, ex.Message));
            }
        });

        var push2 = Task.Run(() =>
        {
            try
            {
                var output = RunGit(clone2Dir, "push origin main");
                results.Add((2, true, output));
            }
            catch (Exception ex)
            {
                results.Add((2, false, ex.Message));
            }
        });

        await Task.WhenAll(push1, push2);

        // Assert - One should succeed, one should fail with non-fast-forward
        var resultsList = results.ToList();
        Assert.Equal(2, resultsList.Count);
        
        var successCount = resultsList.Count(r => r.success);
        var failCount = resultsList.Count(r => !r.success);
        
        // At least one should succeed
        Assert.True(successCount >= 1, "At least one push should succeed");
        
        // If one failed, it should be due to non-fast-forward
        if (failCount > 0)
        {
            var failedPush = resultsList.First(r => !r.success);
            Assert.Contains("non-fast-forward", failedPush.output, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task NonFastForwardPush_IsRejected()
    {
        // Arrange
        CreateSourceRepository("ff-test", new[] { ("initial.txt", "initial") });
        await StartServerAsync(enableReceivePack: true);

        var clone1Dir = Path.Combine(_clientWorkingDir, "clone1");
        var clone2Dir = Path.Combine(_clientWorkingDir, "clone2");
        
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/ff-test.git {clone1Dir}");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/ff-test.git {clone2Dir}");

        ConfigureGit(clone1Dir);
        ConfigureGit(clone2Dir);

        // Clone1: Create and push commit
        File.WriteAllText(Path.Combine(clone1Dir, "file1.txt"), "from clone1");
        RunGit(clone1Dir, "add file1.txt");
        RunGit(clone1Dir, "commit -m \"Commit from clone1\" --quiet");
        RunGit(clone1Dir, "push origin main");

        // Clone2: Create commit (now outdated) and try to push
        File.WriteAllText(Path.Combine(clone2Dir, "file2.txt"), "from clone2");
        RunGit(clone2Dir, "add file2.txt");
        RunGit(clone2Dir, "commit -m \"Commit from clone2\" --quiet");

        // Act & Assert - Push should be rejected
        var exception = Assert.Throws<InvalidOperationException>(() =>
            RunGit(clone2Dir, "push origin main"));
        
        // Git CLI shows different error messages depending on version, but it should indicate rejection
        Assert.True(
            exception.Message.Contains("non-fast-forward", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("rejected", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("failed to push", StringComparison.OrdinalIgnoreCase),
            $"Expected push to be rejected with appropriate error, but got: {exception.Message}");
    }

    [Fact]
    public async Task FastForwardPush_Succeeds()
    {
        // Arrange
        CreateSourceRepository("ff-ok-test", new[] { ("initial.txt", "initial") });
        await StartServerAsync(enableReceivePack: true);

        var cloneDir = Path.Combine(_clientWorkingDir, "clone");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/ff-ok-test.git {cloneDir}");
        ConfigureGit(cloneDir);

        // Create a commit
        File.WriteAllText(Path.Combine(cloneDir, "file1.txt"), "content1");
        RunGit(cloneDir, "add file1.txt");
        RunGit(cloneDir, "commit -m \"First commit\" --quiet");

        // Act - Push (should succeed as it's a fast-forward)
        var output = RunGit(cloneDir, "push origin main");

        // Assert
        Assert.Contains("main -> main", output);
    }

    [Fact]
    public async Task PushAfterFetch_Succeeds()
    {
        // Arrange
        CreateSourceRepository("fetch-test", new[] { ("initial.txt", "initial") });
        await StartServerAsync(enableReceivePack: true);

        var clone1Dir = Path.Combine(_clientWorkingDir, "clone1");
        var clone2Dir = Path.Combine(_clientWorkingDir, "clone2");
        
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/fetch-test.git {clone1Dir}");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/fetch-test.git {clone2Dir}");

        ConfigureGit(clone1Dir);
        ConfigureGit(clone2Dir);

        // Clone1: Create and push commit
        File.WriteAllText(Path.Combine(clone1Dir, "file1.txt"), "from clone1");
        RunGit(clone1Dir, "add file1.txt");
        RunGit(clone1Dir, "commit -m \"Commit from clone1\" --quiet");
        RunGit(clone1Dir, "push origin main");

        // Clone2: Fetch, merge, then create and push commit
        RunGit(clone2Dir, "fetch origin");
        RunGit(clone2Dir, "merge origin/main --no-edit");
        
        File.WriteAllText(Path.Combine(clone2Dir, "file2.txt"), "from clone2");
        RunGit(clone2Dir, "add file2.txt");
        RunGit(clone2Dir, "commit -m \"Commit from clone2\" --quiet");

        // Act - Push should succeed now
        var output = RunGit(clone2Dir, "push origin main");

        // Assert
        Assert.Contains("main -> main", output);
    }

    [Fact]
    public async Task StressTest_MultipleConcurrentPushes()
    {
        // Arrange
        CreateSourceRepository("stress-test", new[] { ("initial.txt", "initial") });
        await StartServerAsync(enableReceivePack: true);

        var cloneCount = 5;
        var cloneDirs = new List<string>();
        
        // Create multiple clones
        for (int i = 0; i < cloneCount; i++)
        {
            var cloneDir = Path.Combine(_clientWorkingDir, $"clone{i}");
            RunGit(_clientWorkingDir, $"clone {_serverUrl}/stress-test.git {cloneDir}");
            ConfigureGit(cloneDir);
            cloneDirs.Add(cloneDir);
        }

        var results = new System.Collections.Concurrent.ConcurrentBag<bool>();

        // Act - All clones try to push at the same time
        var tasks = cloneDirs.Select((dir, index) => Task.Run(() =>
        {
            try
            {
                File.WriteAllText(Path.Combine(dir, $"file{index}.txt"), $"content{index}");
                RunGit(dir, $"add file{index}.txt");
                RunGit(dir, "commit -m \"Concurrent commit\" --quiet");
                RunGit(dir, "push origin main");
                results.Add(true);
            }
            catch
            {
                results.Add(false);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        // Assert - At least one should succeed
        Assert.Contains(true, results);
        
        // The ones that failed should be able to succeed after fetching
        var failedClones = cloneDirs.Where((dir, i) => !results.ElementAt(i)).ToList();
        foreach (var cloneDir in failedClones)
        {
            RunGit(cloneDir, "fetch origin");
            RunGit(cloneDir, "rebase origin/main");
            
            // This should succeed now (either push or indicate it's up-to-date)
            var output = RunGit(cloneDir, "push origin main");
            // After rebase, if the content is the same, git might say "Everything up-to-date"
            // or it might actually push if the commit hash is different
            Assert.True(
                output.Contains("main -> main") || output.Contains("Everything up-to-date"),
                $"Expected push to succeed or be up-to-date, but got: {output}");
        }
    }

    [Fact]
    public async Task PushLock_PreventsInterleavedWrites()
    {
        // This test verifies that the push lock prevents reference updates
        // from being interleaved in a way that could cause inconsistency

        // Arrange
        CreateSourceRepository("lock-test", new[] { ("initial.txt", "initial") });
        await StartServerAsync(enableReceivePack: true);

        var clone1Dir = Path.Combine(_clientWorkingDir, "clone1");
        var clone2Dir = Path.Combine(_clientWorkingDir, "clone2");
        
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/lock-test.git {clone1Dir}");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/lock-test.git {clone2Dir}");

        ConfigureGit(clone1Dir);
        ConfigureGit(clone2Dir);

        // Both create commits
        File.WriteAllText(Path.Combine(clone1Dir, "file1.txt"), "content1");
        RunGit(clone1Dir, "add file1.txt");
        RunGit(clone1Dir, "commit -m \"Clone1 commit\" --quiet");

        File.WriteAllText(Path.Combine(clone2Dir, "file2.txt"), "content2");
        RunGit(clone2Dir, "add file2.txt");
        RunGit(clone2Dir, "commit -m \"Clone2 commit\" --quiet");

        var pushResults = new System.Collections.Concurrent.ConcurrentBag<(string clone, bool success)>();

        // Act - Concurrent pushes
        var push1 = Task.Run(() =>
        {
            try
            {
                RunGit(clone1Dir, "push origin main");
                pushResults.Add(("clone1", true));
            }
            catch
            {
                pushResults.Add(("clone1", false));
            }
        });

        var push2 = Task.Run(() =>
        {
            try
            {
                RunGit(clone2Dir, "push origin main");
                pushResults.Add(("clone2", true));
            }
            catch
            {
                pushResults.Add(("clone2", false));
            }
        });

        await Task.WhenAll(push1, push2);

        // Assert - Repository should be in a consistent state
        // Verify by cloning and checking
        var verifyDir = Path.Combine(_clientWorkingDir, "verify");
        RunGit(_clientWorkingDir, $"clone {_serverUrl}/lock-test.git {verifyDir}");

        // Should be able to enumerate commits without error
        var logOutput = RunGit(verifyDir, "log --oneline");
        Assert.NotEmpty(logOutput);
    }

    private GitRepository CreateSourceRepository(string name, (string path, string content)[] files)
    {
        var bareRepoPath = Path.Combine(_serverRepoRoot, $"{name}.git");
        Directory.CreateDirectory(bareRepoPath);
        
        RunGitInDirectory(bareRepoPath, "init --bare --quiet --initial-branch=main");
        
        var workDir = Path.Combine(Path.GetTempPath(), "temp-source", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        
        try
        {
            RunGitInDirectory(workDir, "init --quiet --initial-branch=main");
            ConfigureGit(workDir);
            
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
            
            RunGitInDirectory(workDir, "add -A");
            RunGitInDirectory(workDir, "commit -m \"Initial commit\" --quiet");
            RunGitInDirectory(workDir, $"remote add origin \"{bareRepoPath}\"");
            RunGitInDirectory(workDir, "push -u origin main --quiet");
        }
        finally
        {
            TestHelper.TryDeleteDirectory(workDir);
        }
        
        return GitRepository.Open(bareRepoPath);
    }

    private async Task StartServerAsync(bool enableUploadPack = true, bool enableReceivePack = false)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        builder.Services.AddGitSmartHttp(options =>
        {
            options.RepositoryRoot = _serverRepoRoot;
            options.EnableUploadPack = enableUploadPack;
            options.EnableReceivePack = enableReceivePack;
        });

        var app = builder.Build();
        app.MapGitSmartHttp("/git/{repository}.git");

        _host = app;
        await _host.StartAsync();

        var addresses = app.Urls;
        _serverUrl = addresses.First() + "/git";
    }

    private void ConfigureGit(string workingDirectory)
    {
        RunGitInDirectory(workingDirectory, "config user.name \"Test User\"");
        RunGitInDirectory(workingDirectory, "config user.email test@test.com");
    }

    private string RunGit(string workingDirectory, string arguments)
    {
        return RunGitInDirectory(workingDirectory, arguments);
    }

    private string RunGitInDirectory(string workingDirectory, string arguments)
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
        TestHelper.SafeStop(_host);
        TestHelper.TryDeleteDirectory(_serverRepoRoot);
        TestHelper.TryDeleteDirectory(_clientWorkingDir);
    }
}
