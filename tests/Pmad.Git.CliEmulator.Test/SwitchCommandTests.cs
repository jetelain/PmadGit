using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;
using Pmad.Git.Tests.Infrastructure;

namespace Pmad.Git.CliEmulator.Test;

public class SwitchCommandTests
{
    [Fact]
    public async Task Switch_ExistingBranch_SwitchesAndOutputsMessage()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["switch", "feature"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Switched to branch 'feature'", response.StdOut);
        Assert.Equal("feature", await repo.GetCurrentBranchNameAsync());
    }

    [Fact]
    public async Task Switch_CurrentBranch_OutputsAlreadyOnBranch()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["switch", "master"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Already on 'master'", response.StdOut);
        Assert.Equal("master", await repo.GetCurrentBranchNameAsync());
    }

    [Fact]
    public async Task Switch_CreateNewBranch_WithDashC_SwitchesAndOutputsMessage()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["switch", "-c", "my-new-branch"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Switched to a new branch 'my-new-branch'", response.StdOut);
        Assert.Equal("my-new-branch", await repo.GetCurrentBranchNameAsync());
    }

    [Fact]
    public async Task Switch_CreateNewBranch_WithStartPoint_CreatesFromStartPoint()
    {
        using var testRepo = GitTestRepository.Create();
        var initialHash = testRepo.Head;
        testRepo.Commit("Second commit", ("second.txt", "2"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["switch", "-c", "branch-from-initial", initialHash.ToString()], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Switched to a new branch 'branch-from-initial'", response.StdOut);
        Assert.Equal("branch-from-initial", await repo.GetCurrentBranchNameAsync());
        Assert.False(File.Exists(Path.Combine(testRepo.WorkingDirectory, "second.txt")));
    }

    [Fact]
    public async Task Checkout_CreateNewBranch_WithDashB_SwitchesAndOutputsMessage()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["checkout", "-b", "checkout-feature"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Switched to a new branch 'checkout-feature'", response.StdOut);
        Assert.Equal("checkout-feature", await repo.GetCurrentBranchNameAsync());
    }

    [Fact]
    public async Task Checkout_ExistingBranch_SwitchesAndOutputsMessage()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["checkout", "feature"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Switched to branch 'feature'", response.StdOut);
        Assert.Equal("feature", await repo.GetCurrentBranchNameAsync());
    }

    [Fact]
    public async Task Switch_NoBranchSpecified_ReturnsError()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["switch"], approval);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("error:", response.StdErr);
    }

    [Fact]
    public async Task Switch_NonExistentBranch_ReturnsError()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["switch", "non-existent"], approval);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("error:", response.StdErr);
    }

    [Fact]
    public async Task Switch_CreateExistingBranch_ReturnsError()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["switch", "-c", "master"], approval);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("error:", response.StdErr);
    }

    [Fact]
    public async Task Switch_DirtyWorkingTree_ReturnsError()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");
        testRepo.Switch("feature");
        testRepo.Commit("Feature commit", ("feature.txt", "feature content"));
        testRepo.Switch("master");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "README.md"), "dirty content");

        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["switch", "feature"], approval);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("error:", response.StdErr);
    }

    [Fact]
    public async Task Switch_ForceCreate_WithDashC_OverwritesExistingBranch()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");
        testRepo.Switch("feature");
        testRepo.Commit("Feature commit", ("feature.txt", "feature content"));
        testRepo.Switch("master");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { UnpushedCommitLossResult = ApprovalResult.Approved };

        // Force create/reset 'feature' to current HEAD of master
        var response = await emulator.InvokeAsync(["switch", "-C", "feature"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Reset branch 'feature'", response.StdOut);
        Assert.Equal("feature", await repo.GetCurrentBranchNameAsync());
        Assert.False(File.Exists(Path.Combine(testRepo.WorkingDirectory, "feature.txt")));
    }

    [Fact]
    public async Task Checkout_ForceCreate_WithDashB_OverwritesExistingBranch()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");
        testRepo.Switch("feature");
        testRepo.Commit("Feature commit", ("feature.txt", "feature content"));
        testRepo.Switch("master");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { UnpushedCommitLossResult = ApprovalResult.Approved };

        var response = await emulator.InvokeAsync(["checkout", "-B", "feature"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Reset branch 'feature'", response.StdOut);
        Assert.Equal("feature", await repo.GetCurrentBranchNameAsync());
    }

    [Fact]
    public async Task Switch_Detach_WithDashD_DetachesHead()
    {
        using var testRepo = GitTestRepository.Create();
        var initialHash = testRepo.Head;
        testRepo.Commit("Commit 2", ("f2.txt", "2"));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["switch", "-d", initialHash.ToString()], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains($"HEAD is now at {initialHash.ToString()[..7]}", response.StdOut);
        Assert.True(await repo.IsHeadDetachedAsync());
        Assert.False(File.Exists(Path.Combine(testRepo.WorkingDirectory, "f2.txt")));
    }

    [Fact]
    public async Task Checkout_Detach_WithDetachFlag_DetachesHead()
    {
        using var testRepo = GitTestRepository.Create();
        var initialHash = testRepo.Head;

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["checkout", "--detach", initialHash.ToString()], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains($"HEAD is now at {initialHash.ToString()[..7]}", response.StdOut);
        Assert.True(await repo.IsHeadDetachedAsync());
    }

    [Fact]
    public async Task Switch_DiscardChanges_DiscardsModificationsAndSwitches()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");
        testRepo.Switch("feature");
        testRepo.Commit("Feature commit", ("feature.txt", "feature content"));
        testRepo.Switch("master");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "README.md"), "dirty changes");

        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { DiscardLocalChangesResult = ApprovalResult.Approved };

        var response = await emulator.InvokeAsync(["switch", "--discard-changes", "feature"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Switched to branch 'feature'", response.StdOut);
        Assert.Equal("feature", await repo.GetCurrentBranchNameAsync());
        Assert.Equal("seed", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "README.md")));
    }

    [Fact]
    public async Task Checkout_Force_DiscardsModificationsAndSwitches()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");
        testRepo.Switch("feature");
        testRepo.Commit("Feature commit", ("feature.txt", "feature content"));
        testRepo.Switch("master");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "README.md"), "dirty changes");

        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { DiscardLocalChangesResult = ApprovalResult.Approved };

        var response = await emulator.InvokeAsync(["checkout", "-f", "feature"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Switched to branch 'feature'", response.StdOut);
        Assert.Equal("feature", await repo.GetCurrentBranchNameAsync());
        Assert.Equal("seed", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "README.md")));
    }
}
