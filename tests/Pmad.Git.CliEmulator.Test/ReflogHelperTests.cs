using System.IO;
using Pmad.Git.CliEmulator.Internal;

namespace Pmad.Git.CliEmulator.Test;

public class ReflogHelperTests
{
    [Fact]
    public async Task RecordCheckoutAsync_And_GetPreviousBranchAsync_NormalFlow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ReflogTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            await ReflogHelper.RecordCheckoutAsync(tempDir, "main", "feature");

            var prev = await ReflogHelper.GetPreviousBranchAsync(tempDir);
            Assert.Equal("main", prev);

            // Record another checkout
            await ReflogHelper.RecordCheckoutAsync(tempDir, "feature", "bugfix");
            prev = await ReflogHelper.GetPreviousBranchAsync(tempDir);
            Assert.Equal("feature", prev);
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
    public async Task RecordCheckoutAsync_WithNullOrDash_DoesNotWritePrevHead()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ReflogTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            await ReflogHelper.RecordCheckoutAsync(tempDir, null, "main");
            Assert.False(File.Exists(Path.Combine(tempDir, "PREV_HEAD")));

            await ReflogHelper.RecordCheckoutAsync(tempDir, "-", "feature");
            Assert.False(File.Exists(Path.Combine(tempDir, "PREV_HEAD")));

            var prev = await ReflogHelper.GetPreviousBranchAsync(tempDir);
            Assert.Null(prev);
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
    public async Task GetPreviousBranchAsync_FallbackToPrevHead()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ReflogTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "PREV_HEAD"), "dev-branch\n");

            var prev = await ReflogHelper.GetPreviousBranchAsync(tempDir);
            Assert.Equal("dev-branch", prev);
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
    public async Task GetPreviousBranchAsync_EmptyDir_ReturnsNull()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ReflogTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var prev = await ReflogHelper.GetPreviousBranchAsync(tempDir);
            Assert.Null(prev);
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
    public async Task RecordCheckoutAsync_InvalidPath_DoesNotThrow()
    {
        // Should handle exceptions quietly without throwing
        await ReflogHelper.RecordCheckoutAsync("Z:\\nonexistent_invalid_drive_path\\test", "main", "feature");
    }
}
