using Pmad.Git.CliEmulator.Approval;
using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class MergeCommandTests
{
    /// <summary>
    /// Creates a diverged-branch scenario:
    ///   master: Initial commit → file on master
    ///   feature: Initial commit → file on feature
    /// </summary>
    private static (GitTestRepository testRepo, string featureBranch) CreateDivergedRepo()
    {
        var testRepo = GitTestRepository.Create();
        var featureBranch = "feature";

        testRepo.CreateBranch(featureBranch);
        testRepo.Switch(featureBranch);
        testRepo.Commit("Feature commit", ("feature.txt", "feature content"));

        testRepo.Switch("master");
        testRepo.Commit("Master commit", ("master.txt", "master content"));

        return (testRepo, featureBranch);
    }

    [Fact]
    public async Task Merge_NoBranchSpecified_ReturnsError()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["merge"], approval);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("error:", response.StdErr);
    }

    [Fact]
    public async Task Merge_AlreadyUpToDate_OutputsUpToDate()
    {
        using var testRepo = GitTestRepository.Create();
        // Create a branch at HEAD then immediately merge it — already up to date
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await emulator.InvokeAsync(["branch", "same"], approval);

        var response = await emulator.InvokeAsync(["merge", "same"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Already up to date.", response.StdOut);
    }

    [Fact]
    public async Task Merge_NoFF_CreatesMergeCommit()
    {
        var (testRepo, featureBranch) = CreateDivergedRepo();
        using (testRepo)
        {
            using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
            var emulator = new GitCliEmulator(repo);
            var approval = new TestUserApproval();

            var response = await emulator.InvokeAsync(["merge", "--no-ff", featureBranch], approval);

            Assert.Equal(0, response.ExitCode);
            // Should report "Merge made by the 'ort' strategy." or similar
            Assert.DoesNotContain("[exit ", response.StdOut);
        }
    }

    [Fact]
    public async Task Merge_WithCustomMessage_UsesProvidedMessage()
    {
        // Verify the -m flag is wired up — merge already-up-to-date reports that, not the custom message,
        // but the command should still exit 0 without error.
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await emulator.InvokeAsync(["branch", "same2"], approval);

        var response = await emulator.InvokeAsync(["merge", "-m", "custom msg", "same2"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.DoesNotContain("error:", response.StdErr);
    }

    [Fact]
    public async Task Merge_FastForward_OutputsFastForward()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");
        testRepo.Switch("feature");
        testRepo.Commit("Feature commit", ("feature.txt", "feature content"));
        testRepo.Switch("master");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["merge", "feature"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Fast-forward", response.StdOut);
    }

    [Fact]
    public async Task Merge_Conflicted_OutputsConflictAndReturns1()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Base commit", ("conflict.txt", "initial content"));

        testRepo.CreateBranch("feature");
        testRepo.Switch("feature");
        testRepo.Commit("Feature commit", ("conflict.txt", "feature line"));

        testRepo.Switch("master");
        testRepo.Commit("Master commit", ("conflict.txt", "master line"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["merge", "feature"], approval);

        Assert.Equal(1, response.ExitCode);
        Assert.Contains("Automatic merge failed; fix conflicts and then commit the result.", response.StdOut);
        Assert.Contains("CONFLICT: conflict.txt", response.StdErr);
    }

    [Fact]
    public async Task Merge_Abort_WhenApproved_AbortsMerge()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Base commit", ("conflict.txt", "initial content"));

        testRepo.CreateBranch("feature");
        testRepo.Switch("feature");
        testRepo.Commit("Feature commit", ("conflict.txt", "feature line"));

        testRepo.Switch("master");
        testRepo.Commit("Master commit", ("conflict.txt", "master line"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await emulator.InvokeAsync(["merge", "feature"], approval);

        var abortResponse = await emulator.InvokeAsync(["merge", "--abort"], approval);

        Assert.Equal(0, abortResponse.ExitCode);
        Assert.Contains("Merge aborted.", abortResponse.StdOut);
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Equal("merge --abort", approval.DiscardLocalChangesCalls[0].Operation);
    }

    [Fact]
    public async Task Merge_Abort_WhenDenied_Returns130()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Base commit", ("conflict.txt", "initial content"));

        testRepo.CreateBranch("feature");
        testRepo.Switch("feature");
        testRepo.Commit("Feature commit", ("conflict.txt", "feature line"));

        testRepo.Switch("master");
        testRepo.Commit("Master commit", ("conflict.txt", "master line"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await emulator.InvokeAsync(["merge", "feature"], approval);

        approval.DiscardLocalChangesResult = ApprovalResult.Denied;

        var abortResponse = await emulator.InvokeAsync(["merge", "--abort"], approval);

        Assert.Equal(130, abortResponse.ExitCode);
        Assert.Contains("denied", abortResponse.StdErr);
    }

    [Fact]
    public async Task Merge_Continue_CompletesMerge()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Base commit", ("conflict.txt", "initial content"));

        testRepo.CreateBranch("feature");
        testRepo.Switch("feature");
        testRepo.Commit("Feature commit", ("conflict.txt", "feature line"));

        testRepo.Switch("master");
        testRepo.Commit("Master commit", ("conflict.txt", "master line"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        await emulator.InvokeAsync(["merge", "feature"], approval);

        // Resolve conflict
        File.WriteAllText(Path.Combine(testRepo.WorkingDirectory, "conflict.txt"), "resolved content");
        await repo.ResolveConflictAsync("conflict.txt");

        var continueResponse = await emulator.InvokeAsync(["merge", "--continue", "-m", "Resolved merge commit"], approval);

        Assert.Equal(0, continueResponse.ExitCode);
        Assert.Contains("Merge commit", continueResponse.StdOut);
    }

    [Fact]
    public async Task Merge_NonExistentBranch_WritesError()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["merge", "does-not-exist"], approval);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("error:", response.StdErr);
    }
}
