using Pmad.Git.Cli.Test.Infrastructure;

namespace Pmad.Git.Cli.Test;

public class GitCliRepositoryCloneTests
{
    [Fact]
    public async Task CloneAsync_WithNonExistentTargetPath_ClonesRepository()
    {
        using var source = GitCliTestRepository.Create();
        var targetPath = Path.Combine(Path.GetTempPath(), "PmadGitCliCloneTests", Guid.NewGuid().ToString("N"));

        try
        {
            var repository = await GitCliRepository.CloneAsync(source.WorkingDirectory, targetPath);

            Assert.Equal(targetPath, repository.RootPath);
            Assert.True(Directory.Exists(Path.Combine(targetPath, ".git")));
            Assert.True(File.Exists(Path.Combine(targetPath, "README.md")));
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetPath);
        }
    }

    [Fact]
    public async Task CloneAsync_WithBranch_ChecksOutRequestedBranch()
    {
        using var source = GitCliTestRepository.Create();
        source.RunGit("branch feature-branch");
        var targetPath = Path.Combine(Path.GetTempPath(), "PmadGitCliCloneTests", Guid.NewGuid().ToString("N"));

        try
        {
            var repository = await GitCliRepository.CloneAsync(source.WorkingDirectory, targetPath, branch: "feature-branch");

            var currentBranch = await repository.GetCurrentBranchAsync();

            Assert.Equal("feature-branch", currentBranch);
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetPath);
        }
    }

    [Fact]
    public async Task CloneAsync_WithRemoteName_UsesRequestedRemoteName()
    {
        using var source = GitCliTestRepository.Create();
        var targetPath = Path.Combine(Path.GetTempPath(), "PmadGitCliCloneTests", Guid.NewGuid().ToString("N"));

        try
        {
            var repository = await GitCliRepository.CloneAsync(source.WorkingDirectory, targetPath, remoteName: "upstream");

            var remotes = await repository.RunAsync("remote");

            Assert.Contains("upstream", remotes);
            Assert.DoesNotContain("origin", remotes);
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetPath);
        }
    }

    [Fact]
    public async Task CloneAsync_WithNullOrWhitespaceRemoteUrl_ThrowsArgumentException()
    {
        var targetPath = Path.Combine(Path.GetTempPath(), "PmadGitCliCloneTests", Guid.NewGuid().ToString("N"));

        await Assert.ThrowsAsync<ArgumentException>(() => GitCliRepository.CloneAsync("", targetPath));
    }

    [Fact]
    public async Task CloneAsync_WithNullOrWhitespaceTargetPath_ThrowsArgumentException()
    {
        using var source = GitCliTestRepository.Create();

        await Assert.ThrowsAsync<ArgumentException>(() => GitCliRepository.CloneAsync(source.WorkingDirectory, "   "));
    }

    [Fact]
    public async Task CloneAsync_WithInvalidRemote_ThrowsGitCliException()
    {
        var targetPath = Path.Combine(Path.GetTempPath(), "PmadGitCliCloneTests", Guid.NewGuid().ToString("N"));

        try
        {
            await Assert.ThrowsAsync<GitCliException>(
                () => GitCliRepository.CloneAsync("not-a-valid-remote-url", targetPath));
        }
        finally
        {
            TestHelper.TryDeleteDirectory(targetPath);
        }
    }
}
