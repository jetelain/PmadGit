using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class RemoteCommandTests
{
    [Fact]
    public async Task Remote_WithoutVerbose_ListsRemoteNames()
    {
        using var testRepo = GitTestRepository.Create();
        // Write a fake remote config entry
        var configPath = Path.Combine(testRepo.WorkingDirectory, ".git", "config");
        File.AppendAllText(configPath, "\n[remote \"origin\"]\n\turl = https://example.com/repo.git\n");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["remote"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("origin", response.StdOut);
        // Should NOT show the URL in non-verbose mode
        Assert.DoesNotContain("https://", response.StdOut);
    }

    [Fact]
    public async Task Remote_Verbose_ShowsUrl()
    {
        using var testRepo = GitTestRepository.Create();
        var configPath = Path.Combine(testRepo.WorkingDirectory, ".git", "config");
        File.AppendAllText(configPath, "\n[remote \"origin\"]\n\turl = https://example.com/repo.git\n");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["remote", "-v"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("https://example.com/repo.git", response.StdOut);
        Assert.Contains("(fetch)", response.StdOut);
        Assert.Contains("(push)", response.StdOut);
    }
}
