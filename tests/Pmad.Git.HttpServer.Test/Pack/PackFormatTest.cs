using System.Diagnostics;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Test.Pack;

public class PackFormatTest : IDisposable
{
    private readonly string _workingDirectory;
    private readonly string _gitDirectory;

    public PackFormatTest()
    {
        _workingDirectory = Path.Combine(Path.GetTempPath(), "PmadGitPackFormatTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workingDirectory);
        _gitDirectory = Path.Combine(_workingDirectory, ".git");
        RunGit("init --quiet");
        RunGit("config user.name \"Test User\"");
        RunGit("config user.email test@example.com");
    }

    [Fact]
    public void InspectPackFormat()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "test.txt"), "test content");
        RunGit("add test.txt");
        RunGit("commit -m \"Test\" --quiet");
        RunGit("repack -a -d -q");

        var packDir = Path.Combine(_gitDirectory, "objects", "pack");
        var packs = Directory.GetFiles(packDir, "*.pack");

        if (packs.Length == 0)
        {
            return; // No pack created
        }

        var packData = File.ReadAllBytes(packs[0]);

        // Header should be "PACK" + version (4 bytes) + object count (4 bytes) = 12 bytes
        Assert.True(packData.Length >= 12, $"Pack file too small: {packData.Length} bytes");
        
        Assert.Equal((byte)'P', packData[0]);
        Assert.Equal((byte)'A', packData[1]);
        Assert.Equal((byte)'C', packData[2]);
        Assert.Equal((byte)'K', packData[3]);

        var version = (packData[4] << 24) | (packData[5] << 16) | (packData[6] << 8) | packData[7];
        var objectCount = (uint)((packData[8] << 24) | (packData[9] << 16) | (packData[10] << 8) | packData[11]);

        Assert.Equal(2, version);
        Assert.True(objectCount > 0, $"Object count is {objectCount}");
        Assert.True(objectCount <= 10, $"Object count is {objectCount}, seems too high for a simple commit");
        
        // Output for debugging
        System.Diagnostics.Debug.WriteLine($"Pack file size: {packData.Length} bytes, Object count: {objectCount}");
    }

    private string RunGit(string arguments)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = _workingDirectory,
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
            throw new InvalidOperationException($"git {arguments} failed with exit code {process.ExitCode}:{Environment.NewLine}{error}");
        }

        return string.IsNullOrEmpty(output) ? error : output;
    }

    public void Dispose()
    {
        TestHelper.TryDeleteDirectory(_workingDirectory);
    }
}
