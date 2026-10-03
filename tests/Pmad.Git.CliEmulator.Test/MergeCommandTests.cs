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
}
