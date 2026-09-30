using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class CommitCommandTests
{
    [Fact]
    public async Task Commit_WithoutMessage_ReturnsError()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var newFilePath = Path.Combine(testRepo.WorkingDirectory, "newfile.txt");
        File.WriteAllText(newFilePath, "contents");
        await emulator.InvokeAsync(["add", "newfile.txt"], approval);

        // No -m flag
        var commitResponse = await emulator.InvokeAsync(["commit"], approval);

        Assert.NotEqual(0, commitResponse.ExitCode);
        Assert.Contains("error:", commitResponse.StdErr);
    }

    [Fact]
    public async Task Commit_All_StagesTrackedModificationsAndCommits()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Modify an already-tracked file without staging it
        var readmePath = Path.Combine(testRepo.WorkingDirectory, "README.md");
        File.WriteAllText(readmePath, "modified by test");

        // -a should auto-stage the tracked modification
        var commitResponse = await emulator.InvokeAsync(["commit", "-a", "-m", "Auto-stage commit"], approval);

        Assert.Equal(0, commitResponse.ExitCode);
        Assert.Contains("Auto-stage commit", commitResponse.StdOut);

        // Working tree should now be clean
        var statusResponse = await emulator.InvokeAsync(["status"], approval);
        Assert.Contains("nothing to commit", statusResponse.StdOut);
    }

    [Fact]
    public async Task Commit_Amend_OnPushedCommit_RequiresHistoryRewriteApproval()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval
        {
            HistoryRewriteResult = ApprovalResult.Approved
        };

        // Mark the HEAD commit as "pushed" by creating a matching remote tracking ref
        var headHash = testRepo.Head.ToString();
        var remoteRefDir = Path.Combine(testRepo.WorkingDirectory, ".git", "refs", "remotes", "origin");
        Directory.CreateDirectory(remoteRefDir);
        File.WriteAllText(Path.Combine(remoteRefDir, "master"), headHash);

        var response = await emulator.InvokeAsync(["commit", "--amend", "-m", "Amended pushed commit"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Single(approval.HistoryRewriteCalls);
        Assert.Equal("commit --amend", approval.HistoryRewriteCalls[0].Operation);
    }

    [Fact]
    public async Task Commit_Amend_OnPushedCommit_WhenDenied_ReturnsExitCode130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval
        {
            HistoryRewriteResult = ApprovalResult.Denied
        };

        // Mark as pushed
        var headHash = testRepo.Head.ToString();
        var remoteRefDir = Path.Combine(testRepo.WorkingDirectory, ".git", "refs", "remotes", "origin");
        Directory.CreateDirectory(remoteRefDir);
        File.WriteAllText(Path.Combine(remoteRefDir, "master"), headHash);

        var response = await emulator.InvokeAsync(["commit", "--amend", "-m", "Denied amend"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("denied", response.StdErr);
    }
}
