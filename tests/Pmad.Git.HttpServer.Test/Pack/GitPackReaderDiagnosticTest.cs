using System.Diagnostics;
using Pmad.Git.HttpServer.Pack;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Test.Pack;

public class GitPackReaderDiagnosticTest : IDisposable
{
    private readonly string _workingDirectory;
    private readonly string _gitDirectory;

    public GitPackReaderDiagnosticTest()
    {
        _workingDirectory = Path.Combine(Path.GetTempPath(), "PmadGitPackDiag", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workingDirectory);
        _gitDirectory = Path.Combine(_workingDirectory, ".git");
        RunGit("init --quiet");
        RunGit("config user.name \"Test\"");
        RunGit("config user.email test@test.com");
    }

    [Fact]
    public async Task DiagnoseMultipleCommits()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "file1.txt"), "Content 1");
        RunGit("add file1.txt");
        RunGit("commit -m \"First\" --quiet");

        File.WriteAllText(Path.Combine(_workingDirectory, "file2.txt"), "Content 2");
        RunGit("add file2.txt");
        RunGit("commit -m \"Second\" --quiet");

        File.WriteAllText(Path.Combine(_workingDirectory, "file3.txt"), "Content 3");
        RunGit("add file3.txt");
        RunGit("commit -m \"Third\" --quiet");

        RunGit("repack -a -d -q");

        var packDir = Path.Combine(_gitDirectory, "objects", "pack");
        var packs = Directory.GetFiles(packDir, "*.pack");
        Assert.NotEmpty(packs);

        var packData = File.ReadAllBytes(packs[0]);
        var objectCount = (uint)((packData[8] << 24) | (packData[9] << 16) | (packData[10] << 8) | packData[11]);

        System.Diagnostics.Debug.WriteLine($"Pack file size: {packData.Length}, Object count: {objectCount}");

        // Try to read with GitPackReader
        var targetDir = Path.Combine(Path.GetTempPath(), "PmadGitPackTarget", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(targetDir);
        try
        {
            RunGitInDirectory(targetDir, "init --quiet");
            RunGitInDirectory(targetDir, "config user.name \"Test\"");
            RunGitInDirectory(targetDir, "config user.email test@test.com");

            using var packStream = new FileStream(packs[0], FileMode.Open, FileAccess.Read, FileShare.Read);
            var repository = GitRepository.Open(targetDir);
            var reader = new GitPackReader();

            var created = await reader.ReadAsync(repository, packStream, CancellationToken.None);

            Assert.NotEmpty(created);
            System.Diagnostics.Debug.WriteLine($"Created {created.Count} objects");
        }
        finally
        {
            try { Directory.Delete(targetDir, true); } catch { }
        }
    }

    private string RunGit(string arguments)
    {
        return RunGitInDirectory(_workingDirectory, arguments);
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

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git failed: {error}");
        }

        return string.IsNullOrEmpty(output) ? error : output;
    }

    public void Dispose()
    {
        try { Directory.Delete(_workingDirectory, true); } catch { }
    }
}
