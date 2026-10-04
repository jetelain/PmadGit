using System;
using System.IO;
using System.Threading.Tasks;
using Pmad.Git.LocalRepositories;
using Pmad.Git.LocalRepositories.Config;
using Pmad.Git.LocalRepositories.Pack;
using Pmad.Git.Tests.Infrastructure;
using Xunit;

namespace Pmad.Git.LocalRepositories.Test;

public sealed class GitSecurityAuditTests
{
    [Theory]
    [InlineData(".git")]
    [InlineData(".git/hooks/pre-commit")]
    [InlineData("sub/.git/config")]
    [InlineData(".GIT/config")]
    [InlineData("git~1")]
    [InlineData("git~1/hooks/post-checkout")]
    [InlineData(".git.")]
    [InlineData("file.txt:stream")]
    public void NormalizeAndValidateRelativePath_RejectsDotGitAndAlternateStreams(string maliciousPath)
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        var ex = Assert.Throws<ArgumentException>(() => manager.NormalizeAndValidateRelativePath(maliciousPath));
        Assert.Contains("forbidden", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NormalizeAndValidateRelativePath_RejectsSymlinkTargetingGitDirectory()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        var manager = repo.IndexManager!;

        var linkPath = Path.Combine(testRepo.WorkingDirectory, "gitlink");
        var gitDir = Path.Combine(testRepo.WorkingDirectory, ".git");

        if (TryCreateDirectoryLink(linkPath, gitDir))
        {
            var ex = Assert.Throws<ArgumentException>(() => manager.NormalizeAndValidateRelativePath("gitlink/config"));
            Assert.Contains("symlink", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                process?.WaitForExit();
                return Directory.Exists(linkPath);
            }
            else
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
                return true;
            }
        }
        catch
        {
            return false;
        }
    }

    [Theory]
    [InlineData("../outside_file")]
    [InlineData("../../windows/win.ini")]
    [InlineData("refs/heads/../../../secret")]
    [InlineData("C:/absolute/path")]
    [InlineData("/etc/passwd")]
    public async Task TryResolveReferenceAsync_RejectsPathTraversalOutsideGitDirectory(string maliciousRef)
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Initial", ("file.txt", "content"));
        var repo = GitRepository.Open(testRepo.WorkingDirectory);

        var result = await repo.ReferenceStore.TryResolveReferenceAsync(maliciousRef);
        Assert.Null(result);
    }

    [Fact]
    public async Task GitConfigFile_WriteToFileAsync_WritesAtomicallyWithoutTempFilesRemaining()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var configPath = Path.Combine(tempDir, "config");
            var config = new GitConfigFile();
            config.SetValue("core", null, "bare", "false");
            config.SetValue("user", null, "name", "TestUser");

            await config.WriteToFileAsync(configPath);

            Assert.True(File.Exists(configPath));
            var content = await File.ReadAllTextAsync(configPath);
            Assert.Contains("[core]", content);
            Assert.Contains("TestUser", content);

            // Verify no leftover .tmp files
            var tmpFiles = Directory.GetFiles(tempDir, "*.tmp");
            Assert.Empty(tmpFiles);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void ReadVariableLength_TruncatedPayload_ThrowsInvalidDataException()
    {
        // 0x81 indicates more bytes follow, but input ends abruptly
        byte[] truncated = [0x81];
        var cursor = 0;

        Assert.Throws<InvalidDataException>(() => GitPackObjectReader.ReadVariableLength(truncated, ref cursor));
    }

    [Fact]
    public void ReadVariableLength_ExcessiveShift_ThrowsInvalidDataException()
    {
        // 10 bytes with 0x80 bit set causes shift to reach 70 (> 63)
        byte[] overflow = [0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81, 0x81];
        var cursor = 0;

        Assert.Throws<InvalidDataException>(() => GitPackObjectReader.ReadVariableLength(overflow, ref cursor));
    }
}
