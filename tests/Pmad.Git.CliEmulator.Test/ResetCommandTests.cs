using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class ResetCommandTests
{
    [Fact]
    public async Task Reset_Mixed_UnstagesStagedFiles()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Stage a new file
        var newFile = Path.Combine(testRepo.WorkingDirectory, "staged.txt");
        File.WriteAllText(newFile, "content");
        await emulator.InvokeAsync(["add", "staged.txt"], approval);

        var statusBefore = await emulator.InvokeAsync(["status"], approval);
        Assert.Contains("Changes to be committed:", statusBefore.StdOut);

        // reset --mixed HEAD (default) unstages the file
        var response = await emulator.InvokeAsync(["reset", "--mixed", "HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("HEAD is now at", response.StdOut);

        var statusAfter = await emulator.InvokeAsync(["status"], approval);
        Assert.DoesNotContain("Changes to be committed:", statusAfter.StdOut);
        Assert.Contains("Untracked files:", statusAfter.StdOut);
    }

    [Fact]
    public async Task Reset_Soft_KeepsStagedChanges()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 2", ("f2.txt", "content"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Soft reset to the initial commit using HEAD~1 ancestor notation
        var response = await emulator.InvokeAsync(["reset", "--soft", "HEAD~1"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("HEAD is now at", response.StdOut);

        // f2.txt should show up as staged (to be committed)
        var status = await emulator.InvokeAsync(["status"], approval);
        Assert.Contains("Changes to be committed:", status.StdOut);
    }

    [Fact]
    public async Task Reset_Hard_ResetsWorkingTree()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Modify a tracked file
        var readmePath = Path.Combine(testRepo.WorkingDirectory, "README.md");
        File.WriteAllText(readmePath, "overwritten");

        var response = await emulator.InvokeAsync(["reset", "--hard", "HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("HEAD is now at", response.StdOut);
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Equal("seed", File.ReadAllText(readmePath));
    }

    [Fact]
    public async Task Reset_DefaultMode_IsMixed()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var newFile = Path.Combine(testRepo.WorkingDirectory, "staged2.txt");
        File.WriteAllText(newFile, "content");
        await emulator.InvokeAsync(["add", "staged2.txt"], approval);

        // No mode flag — defaults to mixed
        var response = await emulator.InvokeAsync(["reset", "HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        var statusAfter = await emulator.InvokeAsync(["status"], approval);
        Assert.DoesNotContain("Changes to be committed:", statusAfter.StdOut);
    }

    [Fact]
    public async Task Reset_Hard_WithNoLocalChanges_SucceedsWithoutApproval()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // No modifications, so no approval needed
        var response = await emulator.InvokeAsync(["reset", "--hard", "HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Empty(approval.DiscardLocalChangesCalls);
    }

    [Fact]
    public async Task Reset_Hard_WhenDenied_ReturnsExitCode130AndPreservesFile()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval
        {
            DiscardLocalChangesResult = ApprovalResult.Denied
        };

        var readmePath = Path.Combine(testRepo.WorkingDirectory, "README.md");
        File.WriteAllText(readmePath, "modified");

        var response = await emulator.InvokeAsync(["reset", "--hard", "HEAD"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Equal("modified", File.ReadAllText(readmePath));
    }

    [Fact]
    public async Task Reset_ToCommitWithUnpushedLoss_RequiresApproval()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 2", ("f2.txt", "content2"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval
        {
            UnpushedCommitLossResult = ApprovalResult.Approved
        };

        // reset back to initial commit using HEAD~1 — loses "Commit 2" which is unpushed
        var response = await emulator.InvokeAsync(["reset", "--soft", "HEAD~1"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Single(approval.UnpushedCommitLossCalls);
        Assert.Equal("reset --soft", approval.UnpushedCommitLossCalls[0].Operation);
    }
}
