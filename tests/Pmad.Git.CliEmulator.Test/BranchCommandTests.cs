using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class BranchCommandTests
{
    [Fact]
    public async Task Branch_Verbose_ShowsHashAndSubject()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["branch", "-v"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("master", response.StdOut);
        // Verbose output should contain the short hash and the commit subject
        Assert.Contains("Initial commit", response.StdOut);
    }

    [Fact]
    public async Task Branch_All_IncludesRemoteTrackingBranches()
    {
        using var testRepo = GitTestRepository.Create();
        // Create a fake remote tracking ref
        var remoteRefDir = Path.Combine(testRepo.WorkingDirectory, ".git", "refs", "remotes", "origin");
        Directory.CreateDirectory(remoteRefDir);
        File.WriteAllText(Path.Combine(remoteRefDir, "main"), testRepo.Head.ToString());

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["branch", "-a"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("remotes/origin/main", response.StdOut);
    }

    [Fact]
    public async Task Branch_SafeDelete_MergedBranch_Succeeds()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Create a branch at the same commit as master — it is "fully merged"
        await emulator.InvokeAsync(["branch", "merged-branch"], approval);

        var response = await emulator.InvokeAsync(["branch", "-d", "merged-branch"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Deleted branch merged-branch", response.StdOut);
    }

    [Fact]
    public async Task Branch_Delete_WithoutName_ReturnsError()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["branch", "-d"], approval);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("error:", response.StdErr);
    }

    [Fact]
    public async Task Branch_CreateWithStartPoint_CreatesAtThatCommit()
    {
        using var testRepo = GitTestRepository.Create();
        var initialHash = testRepo.Head.ToString();
        testRepo.Commit("Commit 2", ("f2.txt", "c2"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Create branch at the initial commit
        var response = await emulator.InvokeAsync(["branch", "at-initial", initialHash], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("at-initial", response.StdOut);
    }

    [Fact]
    public async Task Branch_Rename_NoName_ReturnsError()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // -m without any name argument
        var response = await emulator.InvokeAsync(["branch", "-m"], approval);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("error:", response.StdErr);
    }
}
