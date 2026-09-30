using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class DiffCommandTests
{
    [Fact]
    public async Task Diff_Staged_ShowsStagedChanges()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Stage a modification
        var readmePath = Path.Combine(testRepo.WorkingDirectory, "README.md");
        File.WriteAllText(readmePath, "staged change");
        await emulator.InvokeAsync(["add", "README.md"], approval);

        var response = await emulator.InvokeAsync(["diff", "--staged"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("+staged change", response.StdOut);
        Assert.Contains("-seed", response.StdOut);
    }

    [Fact]
    public async Task Diff_Cached_IsAliasForStaged()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var readmePath = Path.Combine(testRepo.WorkingDirectory, "README.md");
        File.WriteAllText(readmePath, "cached change");
        await emulator.InvokeAsync(["add", "README.md"], approval);

        var response = await emulator.InvokeAsync(["diff", "--cached"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("+cached change", response.StdOut);
    }

    [Fact]
    public async Task Diff_BetweenTwoCommits_ShowsChanges()
    {
        using var testRepo = GitTestRepository.Create();
        var firstHash = testRepo.Head.ToString();
        testRepo.Commit("Second commit", ("file2.txt", "content2"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // diff between first commit and HEAD
        var response = await emulator.InvokeAsync(["diff", firstHash, "HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("file2.txt", response.StdOut);
    }

    [Fact]
    public async Task Diff_CleanWorkingTree_ProducesEmptyOutput()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["diff"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.True(string.IsNullOrWhiteSpace(response.StdOut));
    }
}
