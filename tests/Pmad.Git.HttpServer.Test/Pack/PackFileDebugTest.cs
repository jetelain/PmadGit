using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Pmad.Git.HttpServer.Test.Pack;

public class PackFileDebugTest : IDisposable
{
    private readonly string _workingDirectory;

    public PackFileDebugTest()
    {
        _workingDirectory = Path.Combine(Path.GetTempPath(), "PmadGitPackDebug", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workingDirectory);
        RunGit("init --quiet");
        RunGit("config user.name \"Test\"");
        RunGit("config user.email test@test.com");
    }

    [Fact]
    public void DebugPackFile()
    {
        File.WriteAllText(Path.Combine(_workingDirectory, "test.txt"), "test");
        RunGit("add test.txt");
        RunGit("commit -m \"Test\" --quiet");
        RunGit("repack -a -d -q");

        var packDir = Path.Combine(_workingDirectory, ".git", "objects", "pack");
        var packs = Directory.GetFiles(packDir, "*.pack");
        Assert.NotEmpty(packs);

        var packData = File.ReadAllBytes(packs[0]);
        
        // Parse header
        var version = (packData[4] << 24) | (packData[5] << 16) | (packData[6] << 8) | packData[7];
        var objectCount = (uint)((packData[8] << 24) | (packData[9] << 16) | (packData[10] << 8) | packData[11]);

        Assert.Equal(2, version);
        
        // Calculate hash of everything except last 20 bytes
        using var sha1 = SHA1.Create();
        var hash = sha1.ComputeHash(packData, 0, packData.Length - 20);
        var trailer = packData.Skip(packData.Length - 20).ToArray();

        // Verify checksum matches
        Assert.True(hash.SequenceEqual(trailer), $"Checksum mismatch. Pack size: {packData.Length}, Object count: {objectCount}");

        // Try to manually parse objects
        var pos = 12; // After header
        var objectsRead = 0;
        
        while (objectsRead < objectCount && pos < packData.Length - 20)
        {
            var startPos = pos;
            
            // Read type and size
            var b = packData[pos++];
            var type = (b >> 4) & 0x7;
            long size = b & 0x0F;
            var shift = 4;
            
            while ((b & 0x80) != 0)
            {
                b = packData[pos++];
                size |= (long)(b & 0x7F) << shift;
                shift += 7;
            }

            // Skip the compressed data (we'd need to decompress to know exact length)
            // For now, just report what we found
            objectsRead++;
            
            System.Diagnostics.Debug.WriteLine($"Object {objectsRead}: type={type}, size={size}, pos={startPos}");
            
            // We can't easily skip the zlib data without decompressing, so break here
            break;
        }

        Assert.True(objectsRead > 0, "Should have read at least one object");
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
        TestHelper.TryDeleteDirectory(_workingDirectory);
    }
}
