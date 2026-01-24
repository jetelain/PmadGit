using System;
using System.Diagnostics;
using System.IO;

using Pmad.Git.LocalRepositories;

namespace Pmad.Git.LocalRepositories.Test.Infrastructure;

public enum GitObjectFormat
{
	Sha1,
	Sha256
}

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

    public string RunGit(string arguments)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unab   le to start git process");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments} failed with exit code {process.ExitCode}:{Environment.NewLine}{error}");
        }

        return string.IsNullOrEmpty(output) ? error : output;
    }

    public void Dispose()
    {
        GitTestHelper.TryDeleteDirectory(WorkingDirectory);
    }
}
