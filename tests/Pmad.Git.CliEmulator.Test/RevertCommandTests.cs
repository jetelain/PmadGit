using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class RevertCommandTests
{
    [Fact]
    public async Task Revert_Head_CreatesRevertCommit()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Add file2", ("file2.txt", "content"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["revert", "HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Revert \"Add file2\"", response.StdOut);
        Assert.False(File.Exists(Path.Combine(testRepo.WorkingDirectory, "file2.txt")));
    }

    [Fact]
    public async Task Revert_InvalidRef_ReturnsError()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["revert", "nonexistent-ref"], approval);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("error:", response.StdErr);
    }
}
