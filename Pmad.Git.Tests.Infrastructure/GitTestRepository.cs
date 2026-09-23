using System.Diagnostics;

using Pmad.Git.LocalRepositories;

namespace Pmad.Git.Tests.Infrastructure;

public sealed class GitTestRepository : IDisposable
{
    private GitHash _head;
    private readonly GitObjectFormat _format;

    private GitTestRepository(string workingDirectory, GitObjectFormat format)
    {
        WorkingDirectory = workingDirectory;
        _format = format;
        Initialize();
    }

    public string WorkingDirectory { get; }
    public string GitDirectory => Path.Combine(WorkingDirectory, ".git");
    public GitHash Head => _head;

    public static GitTestRepository Create(GitObjectFormat format = GitObjectFormat.Sha1)
    {
        var root = Path.Combine(Path.GetTempPath(), "PmadGitRepoTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new GitTestRepository(root, format);
    }

    private void Initialize()
    {
        var initArgs = _format == GitObjectFormat.Sha256
            ? "init --quiet --object-format=sha256 --initial-branch=master"
            : "init --quiet --initial-branch=master";
        RunGit(initArgs);
        RunGit("config user.name \"Test User\"");
        RunGit("config user.email test@example.com");
        Commit("Initial commit", ("README.md", "seed"));
    }

    public GitHash Commit(string message, params (string Path, string Content)[] files)
    {
        foreach (var (relativePath, content) in files)
        {
            var fullPath = Path.Combine(WorkingDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, content);
        }

        RunGit("add -A");
        RunGit($"commit -m \"{message}\" --quiet");
        var head = new GitHash(RunGit("rev-parse HEAD").Trim());
        _head = head;
        return head;
    }

    public GitHash RemoveFiles(string message, params string[] filePaths)
    {
        foreach (var relativePath in filePaths)
        {
            var fullPath = Path.Combine(WorkingDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }

        RunGit("add -A");
        RunGit($"commit -m \"{message}\" --quiet");
        var head = new GitHash(RunGit("rev-parse HEAD").Trim());
        _head = head;
        return head;
    }

    public string RunGit(string arguments)
    {
        return TestHelper.RunGit(WorkingDirectory, arguments);
    }

    public void Dispose()
    {
        TestHelper.TryDeleteDirectory(WorkingDirectory);
    }
}
