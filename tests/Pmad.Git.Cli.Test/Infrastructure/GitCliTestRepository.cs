using System.Diagnostics;

namespace Pmad.Git.Cli.Test.Infrastructure;

/// <summary>
/// Helper that creates a throw-away Git repository (backed by the real <c>git</c> executable) for integration tests.
/// </summary>
public sealed class GitCliTestRepository : IDisposable
{
    public const string DefaultBranch = "main";

    private GitCliTestRepository(string workingDirectory)
    {
        WorkingDirectory = workingDirectory;
    }

    public string WorkingDirectory { get; }

    public static GitCliTestRepository CreateBare()
    {
        var root = CreateTempDirectory();
        var repository = new GitCliTestRepository(root);
        repository.RunGit($"init --quiet --bare --initial-branch={DefaultBranch}");
        return repository;
    }

    public static GitCliTestRepository Create(string? initialCommitMessage = "Initial commit")
    {
        var root = CreateTempDirectory();
        var repository = new GitCliTestRepository(root);
        repository.RunGit($"init --quiet --initial-branch={DefaultBranch}");
        repository.RunGit("config user.name \"Test User\"");
        repository.RunGit("config user.email test@example.com");
        if (initialCommitMessage != null)
        {
            repository.Commit(initialCommitMessage, ("README.md", "seed"));
        }
        return repository;
    }

    public static GitCliTestRepository Clone(GitCliTestRepository source)
    {
        var root = CreateTempDirectory();
        Directory.Delete(root);
        RunGitIn(Path.GetTempPath(), $"clone --quiet \"{source.WorkingDirectory}\" \"{root}\"");
        var repository = new GitCliTestRepository(root);
        repository.RunGit("config user.name \"Test User\"");
        repository.RunGit("config user.email test@example.com");
        return repository;
    }

    public void WriteFile(string relativePath, string content)
    {
        var fullPath = ToFullPath(relativePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(fullPath, content);
    }

    public string ReadFile(string relativePath)
    {
        return File.ReadAllText(ToFullPath(relativePath));
    }

    public void Commit(string message, params (string Path, string Content)[] files)
    {
        foreach (var (relativePath, content) in files)
        {
            WriteFile(relativePath, content);
        }
        RunGit("add -A");
        RunGit($"commit -m \"{message}\" --quiet");
    }

    public void AddRemote(string name, GitCliTestRepository other)
    {
        RunGit($"remote add {name} \"{other.WorkingDirectory}\"");
    }

    public string RunGit(string arguments)
    {
        return RunGitIn(WorkingDirectory, arguments);
    }

    public void Dispose()
    {
        GitCliTestHelper.TryDeleteDirectory(WorkingDirectory);
    }

    private string ToFullPath(string relativePath)
    {
        return Path.Combine(WorkingDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string CreateTempDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "PmadGitCliTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string RunGitIn(string workingDirectory, string arguments)
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
}
