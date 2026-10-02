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

    [Fact]
    public async Task Switch_CommitWithoutDetachFlag_FailsWithBranchExpected()
    {
        using var testRepo = GitTestRepository.Create();
        var initialHash = testRepo.Head;

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["switch", initialHash.ToString()], approval);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("a branch is expected, got commit", response.StdErr);
        Assert.Contains("--detach", response.StdErr);
    }

    [Fact]
    public async Task Checkout_CommitWithoutDetachFlag_AutomaticallyDetachesHead()
    {
        using var testRepo = GitTestRepository.Create();
        var initialHash = testRepo.Head;

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["checkout", initialHash.ToString()], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains($"HEAD is now at {initialHash.ToString()[..7]}", response.StdOut);
        Assert.True(await repo.IsHeadDetachedAsync());
    }

    [Fact]
    public async Task Switch_Orphan_StartsEmptyAndGatesOnDirtyChanges()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        await File.WriteAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "README.md"), "dirty");

        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { DiscardLocalChangesResult = ApprovalResult.Approved };

        var response = await emulator.InvokeAsync(["switch", "--orphan", "empty-branch"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Switched to a new branch 'empty-branch'", response.StdOut);
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Equal("empty-branch", await repo.ReferenceStore.GetCurrentBranchNameAsync(allowUnborn: true));
        Assert.False(File.Exists(Path.Combine(testRepo.WorkingDirectory, "README.md")));
    }

    [Fact]
    public async Task Checkout_Orphan_KeepsWorkingTreeFiles()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["checkout", "--orphan", "orphan-branch"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Switched to a new branch 'orphan-branch'", response.StdOut);
        Assert.Equal("orphan-branch", await repo.ReferenceStore.GetCurrentBranchNameAsync(allowUnborn: true));
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "README.md")));
    }

    [Fact]
    public async Task Checkout_RestoreFile_RestoresFromIndexAndGates()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var readmePath = Path.Combine(testRepo.WorkingDirectory, "README.md");
        await File.WriteAllTextAsync(readmePath, "overwritten content");

        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { DiscardLocalChangesResult = ApprovalResult.Approved };

        var response = await emulator.InvokeAsync(["checkout", "--", "README.md"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Contains("README.md", approval.DiscardLocalChangesCalls[0].AffectedFiles);
        Assert.Equal("seed", await File.ReadAllTextAsync(readmePath));
    }

    [Fact]
    public async Task Restore_WithDashS_RestoresFromSourceTreeIsh()
    {
        using var testRepo = GitTestRepository.Create();
        var readmePath = Path.Combine(testRepo.WorkingDirectory, "README.md");
        await File.WriteAllTextAsync(readmePath, "overwritten content");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { DiscardLocalChangesResult = ApprovalResult.Approved };

        var response = await emulator.InvokeAsync(["restore", "-s", "HEAD", "README.md"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Equal("seed", await File.ReadAllTextAsync(readmePath));
    }

    [Fact]
    public async Task Switch_DiscardChanges_FailsIfMergeInProgress()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");

        // Simulate merge in progress
        var mergeHeadPath = Path.Combine(testRepo.WorkingDirectory, ".git", "MERGE_HEAD");
        await File.WriteAllTextAsync(mergeHeadPath, testRepo.Head.ToString());

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { DiscardLocalChangesResult = ApprovalResult.Approved };

        var response = await emulator.InvokeAsync(["switch", "--discard-changes", "feature"], approval);

        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("merge is in progress", response.StdErr);
    }

    [Fact]
    public async Task Checkout_Force_SucceedsIfMergeInProgress()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");

        // Simulate merge in progress
        var mergeHeadPath = Path.Combine(testRepo.WorkingDirectory, ".git", "MERGE_HEAD");
        await File.WriteAllTextAsync(mergeHeadPath, testRepo.Head.ToString());

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { DiscardLocalChangesResult = ApprovalResult.Approved };

        var response = await emulator.InvokeAsync(["checkout", "-f", "feature"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Switched to branch 'feature'", response.StdOut);
        Assert.False(File.Exists(mergeHeadPath));
    }

    [Fact]
    public async Task Checkout_DeletedTrackedFile_RestoresWithoutDoubleDash()
    {
        using var testRepo = GitTestRepository.Create();
        var readmePath = Path.Combine(testRepo.WorkingDirectory, "README.md");
        File.Delete(readmePath);
        Assert.False(File.Exists(readmePath));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { DiscardLocalChangesResult = ApprovalResult.Approved };

        var response = await emulator.InvokeAsync(["checkout", "README.md"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.True(File.Exists(readmePath));
        Assert.Equal("seed", await File.ReadAllTextAsync(readmePath));
    }

    [Fact]
    public async Task Checkout_WithCommitSource_RestoresFileWithoutDoubleDash()
    {
        using var testRepo = GitTestRepository.Create();
        var readmePath = Path.Combine(testRepo.WorkingDirectory, "README.md");
        await File.WriteAllTextAsync(readmePath, "changed locally");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { DiscardLocalChangesResult = ApprovalResult.Approved };

        var response = await emulator.InvokeAsync(["checkout", "HEAD", "README.md"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("seed", await File.ReadAllTextAsync(readmePath));
    }

    [Fact]
    public async Task Switch_And_Checkout_WithRefsHeadsPrefix_NormalizesAndSucceeds()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var responseSwitch = await emulator.InvokeAsync(["switch", "refs/heads/feature"], approval);
        Assert.Equal(0, responseSwitch.ExitCode);
        Assert.Contains("Switched to branch 'feature'", responseSwitch.StdOut);
        Assert.Equal("feature", await repo.GetCurrentBranchNameAsync());

        var responseCheckout = await emulator.InvokeAsync(["checkout", "refs/heads/master"], approval);
        Assert.Equal(0, responseCheckout.ExitCode);
        Assert.Contains("Switched to branch 'master'", responseCheckout.StdOut);
        Assert.Equal("master", await repo.GetCurrentBranchNameAsync());
    }

    [Fact]
    public async Task Switch_RemovesEmptyParentDirectoriesOfDeletedFiles()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Add sub file", ("sub/nested/file.txt", "content"));
        testRepo.CreateBranch("feature");

        testRepo.Switch("feature");
        RunGit(testRepo.WorkingDirectory, "rm sub/nested/file.txt");
        testRepo.Commit("Remove sub file");

        testRepo.Switch("master");
        Assert.True(Directory.Exists(Path.Combine(testRepo.WorkingDirectory, "sub", "nested")));

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["switch", "feature"], approval);
        Assert.Equal(0, response.ExitCode);
        Assert.False(Directory.Exists(Path.Combine(testRepo.WorkingDirectory, "sub")));
    }

    [Fact]
    public async Task Checkout_UntrackedFile_ReportsPathspecErrorAndPreservesFile()
    {
        using var testRepo = GitTestRepository.Create();
        var untrackedPath = Path.Combine(testRepo.WorkingDirectory, "untracked.txt");
        await File.WriteAllTextAsync(untrackedPath, "untracked content");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["checkout", "untracked.txt"], approval);

        Assert.Equal(1, response.ExitCode);
        Assert.Contains("error: pathspec 'untracked.txt' did not match any file(s) known to git", response.StdErr);
        Assert.True(File.Exists(untrackedPath));
        Assert.Equal("untracked content", await File.ReadAllTextAsync(untrackedPath));
        Assert.Empty(approval.DiscardLocalChangesCalls);
    }

    [Fact]
    public async Task Checkout_Force_CollidingUntrackedFile_RequiresApprovalWithUntrackedFile()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");
        testRepo.Switch("feature");
        testRepo.Commit("Add feature file", ("introduced.txt", "feature content"));
        testRepo.Switch("master");

        var collidingPath = Path.Combine(testRepo.WorkingDirectory, "introduced.txt");
        await File.WriteAllTextAsync(collidingPath, "local untracked content");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { DiscardLocalChangesResult = ApprovalResult.Approved };

        var response = await emulator.InvokeAsync(["checkout", "-f", "feature"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Contains("introduced.txt", approval.DiscardLocalChangesCalls[0].AffectedFiles);
    }

    [Fact]
    public async Task Switch_Force_CollidingUntrackedFile_RequiresApprovalWithUntrackedFile()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");
        testRepo.Switch("feature");
        testRepo.Commit("Add feature file", ("introduced.txt", "feature content"));
        testRepo.Switch("master");

        var collidingPath = Path.Combine(testRepo.WorkingDirectory, "introduced.txt");
        await File.WriteAllTextAsync(collidingPath, "local untracked content");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { DiscardLocalChangesResult = ApprovalResult.Approved };

        var response = await emulator.InvokeAsync(["switch", "--discard-changes", "feature"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Contains("introduced.txt", approval.DiscardLocalChangesCalls[0].AffectedFiles);
    }

    [Fact]
    public async Task Checkout_ForceB_PreservesLocalChangesAtCurrentHead()
    {
        using var testRepo = GitTestRepository.Create();
        var readmePath = Path.Combine(testRepo.WorkingDirectory, "README.md");
        await File.WriteAllTextAsync(readmePath, "dirty local changes");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["checkout", "-B", "reset-branch"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Switched to a new branch 'reset-branch'", response.StdOut);
        Assert.Equal("dirty local changes", await File.ReadAllTextAsync(readmePath));
        Assert.Empty(approval.DiscardLocalChangesCalls);
    }

    [Fact]
    public async Task Switch_ForceC_PreservesLocalChangesAtCurrentHead()
    {
        using var testRepo = GitTestRepository.Create();
        var readmePath = Path.Combine(testRepo.WorkingDirectory, "README.md");
        await File.WriteAllTextAsync(readmePath, "dirty local changes");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["switch", "-C", "reset-branch"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Switched to a new branch 'reset-branch'", response.StdOut);
        Assert.Equal("dirty local changes", await File.ReadAllTextAsync(readmePath));
        Assert.Empty(approval.DiscardLocalChangesCalls);
    }

    [Fact]
    public async Task Checkout_Orphan_WithStartPoint_ChecksDirtyFilesAndUntrackedCollisions()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 1", ("file1.txt", "v1"), ("untracked_collision.txt", "v1"));
        var commit1 = testRepo.Head;

        RunGit(testRepo.WorkingDirectory, "rm untracked_collision.txt");
        testRepo.Commit("Commit 2", ("file1.txt", "v2"));

        var collisionPath = Path.Combine(testRepo.WorkingDirectory, "untracked_collision.txt");
        await File.WriteAllTextAsync(collisionPath, "untracked content");

        var file1Path = Path.Combine(testRepo.WorkingDirectory, "file1.txt");
        await File.WriteAllTextAsync(file1Path, "v2 modified");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { DiscardLocalChangesResult = ApprovalResult.Approved };

        // Without -f: should fail with safety check error
        var responseNoForce = await emulator.InvokeAsync(["checkout", "--orphan", "orphan1", commit1.ToString()], approval);
        Assert.Equal(1, responseNoForce.ExitCode);
        Assert.Contains("uncommitted changes", responseNoForce.StdErr);

        // With -f: should require approval and succeed
        var responseForce = await emulator.InvokeAsync(["checkout", "-f", "--orphan", "orphan2", commit1.ToString()], approval);
        Assert.Equal(0, responseForce.ExitCode);
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Contains("file1.txt", approval.DiscardLocalChangesCalls[0].AffectedFiles);
        Assert.Contains("untracked_collision.txt", approval.DiscardLocalChangesCalls[0].AffectedFiles);
    }

    [Fact]
    public async Task Checkout_DoubleDash_Dot_RestoresAllTrackedFiles()
    {
        using var testRepo = GitTestRepository.Create();
        var readmePath = Path.Combine(testRepo.WorkingDirectory, "README.md");
        await File.WriteAllTextAsync(readmePath, "overwritten content");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { DiscardLocalChangesResult = ApprovalResult.Approved };

        var response = await emulator.InvokeAsync(["checkout", "--", "."], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("seed", await File.ReadAllTextAsync(readmePath));
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Contains("README.md", approval.DiscardLocalChangesCalls[0].AffectedFiles);
    }

    [Fact]
    public async Task Checkout_DoubleDash_Directory_RestoresFilesInDirectory()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Add nested", ("sub/nested.txt", "original sub"));

        var nestedPath = Path.Combine(testRepo.WorkingDirectory, "sub", "nested.txt");
        await File.WriteAllTextAsync(nestedPath, "overwritten sub");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { DiscardLocalChangesResult = ApprovalResult.Approved };

        var response = await emulator.InvokeAsync(["checkout", "--", "sub"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("original sub", await File.ReadAllTextAsync(nestedPath));
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Contains("sub/nested.txt", approval.DiscardLocalChangesCalls[0].AffectedFiles);
    }

    [Fact]
    public async Task Checkout_DoubleDash_UntrackedFile_PreservesFileAndReturnsError()
    {
        using var testRepo = GitTestRepository.Create();
        var untrackedPath = Path.Combine(testRepo.WorkingDirectory, "untracked.txt");
        await File.WriteAllTextAsync(untrackedPath, "untracked content");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var responseNoSource = await emulator.InvokeAsync(["checkout", "--", "untracked.txt"], approval);
        Assert.Equal(1, responseNoSource.ExitCode);
        Assert.Contains("error: pathspec 'untracked.txt' did not match any file(s) known to git", responseNoSource.StdErr);
        Assert.True(File.Exists(untrackedPath));
        Assert.Equal("untracked content", await File.ReadAllTextAsync(untrackedPath));

        var responseWithSource = await emulator.InvokeAsync(["checkout", "HEAD", "--", "untracked.txt"], approval);
        Assert.Equal(1, responseWithSource.ExitCode);
        Assert.Contains("error: pathspec 'untracked.txt' did not match any file(s) known to git", responseWithSource.StdErr);
        Assert.True(File.Exists(untrackedPath));
        Assert.Equal("untracked content", await File.ReadAllTextAsync(untrackedPath));
        Assert.Empty(approval.DiscardLocalChangesCalls);
    }

    [Fact]
    public async Task Switch_FileToDirectory_And_DirectoryToFile_TransitionsSuccessfully()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Add file foo", ("foo", "file foo content"));
        testRepo.CreateBranch("file-branch");

        testRepo.CreateBranch("dir-branch");
        testRepo.Switch("dir-branch");
        RunGit(testRepo.WorkingDirectory, "rm foo");
        testRepo.Commit("Replace with foo/bar", ("foo/bar", "dir foo bar content"));

        testRepo.Switch("file-branch");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // file -> dir
        var responseToDir = await emulator.InvokeAsync(["switch", "dir-branch"], approval);
        Assert.Equal(0, responseToDir.ExitCode);
        Assert.False(File.Exists(Path.Combine(testRepo.WorkingDirectory, "foo")));
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "foo", "bar")));
        Assert.Equal("dir foo bar content", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "foo", "bar")));

        // dir -> file
        var responseToFile = await emulator.InvokeAsync(["switch", "file-branch"], approval);
        Assert.Equal(0, responseToFile.ExitCode);
        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "foo")));
        Assert.False(File.Exists(Path.Combine(testRepo.WorkingDirectory, "foo", "bar")));
        Assert.Equal("file foo content", await File.ReadAllTextAsync(Path.Combine(testRepo.WorkingDirectory, "foo")));
    }

    [Fact]
    public async Task Checkout_CancellationDuringCommitProbe_PropagatesCancellation()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var response = await emulator.InvokeAsync(["checkout", "--detach", "nonexistent"], approval, cancellationToken: cts.Token);
        Assert.Equal(130, response.ExitCode);
        Assert.Contains("cancelled", response.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Switch_CancellationDuringCommitProbe_PropagatesCancellation()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var response = await emulator.InvokeAsync(["switch", "nonexistent"], approval, cancellationToken: cts.Token);
        Assert.Equal(130, response.ExitCode);
        Assert.Contains("cancelled", response.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Checkout_Source_UntrackedCollision_RequestsDiscardApproval()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");
        testRepo.Switch("feature");
        testRepo.Commit("Add feature file", ("introduced.txt", "feature content"));
        testRepo.Switch("master");

        var untrackedPath = Path.Combine(testRepo.WorkingDirectory, "introduced.txt");
        await File.WriteAllTextAsync(untrackedPath, "untracked local content");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval { DiscardLocalChangesResult = ApprovalResult.Denied };

        var deniedResponse = await emulator.InvokeAsync(["checkout", "feature", "--", "introduced.txt"], approval);
        Assert.Equal(130, deniedResponse.ExitCode);
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Contains("introduced.txt", approval.DiscardLocalChangesCalls[0].AffectedFiles);
        Assert.Equal("untracked local content", await File.ReadAllTextAsync(untrackedPath));

        approval.DiscardLocalChangesResult = ApprovalResult.Approved;
        var approvedResponse = await emulator.InvokeAsync(["checkout", "feature", "--", "introduced.txt"], approval);
        Assert.Equal(0, approvedResponse.ExitCode);
        Assert.Equal("feature content", await File.ReadAllTextAsync(untrackedPath));
    }

    [Fact]
    public async Task Switch_BranchRefNamespaceConflict_FailsBeforeMutatingWorkspace()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("topic/child");
        testRepo.Commit("Add master file", ("master.txt", "master content"));

        testRepo.CreateBranch("feature");
        testRepo.Switch("feature");
        testRepo.Commit("Add feature file", ("feature.txt", "feature content"));
        testRepo.Switch("master");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["switch", "-c", "topic", "feature"], approval);
        Assert.NotEqual(0, response.ExitCode);
        Assert.Contains("Cannot create reference", response.StdErr);

        Assert.True(File.Exists(Path.Combine(testRepo.WorkingDirectory, "master.txt")));
        Assert.False(File.Exists(Path.Combine(testRepo.WorkingDirectory, "feature.txt")));
        Assert.Equal("master", await repo.GetCurrentBranchNameAsync());
    }

    [Fact]
    public async Task Switch_ExtraPositionalArgument_RejectedWhenNotCreate()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var resp1 = await emulator.InvokeAsync(["switch", "feature", "unexpected"], approval);
        Assert.NotEqual(0, resp1.ExitCode);
        Assert.Contains("only one reference expected", resp1.StdErr);

        var resp2 = await emulator.InvokeAsync(["switch", "--detach", "HEAD", "unexpected"], approval);
        Assert.NotEqual(0, resp2.ExitCode);
        Assert.Contains("only one reference expected", resp2.StdErr);
    }

    [Fact]
    public async Task Switch_TargetFileCollidesWithIgnoredDirectory_DoesNotDeleteIgnoredDirectory()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.CreateBranch("feature");
        testRepo.Switch("feature");
        testRepo.Commit("Add file named build", ("build", "build file content"));
        testRepo.Switch("master");

        var buildDir = Path.Combine(testRepo.WorkingDirectory, "build");
        Directory.CreateDirectory(buildDir);
        var logFile = Path.Combine(buildDir, "output.log");
        await File.WriteAllTextAsync(logFile, "ignored log content");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["switch", "feature"], approval);
        Assert.NotEqual(0, response.ExitCode);
        Assert.True(File.Exists(logFile), "Ignored file inside directory must not be deleted.");
        Assert.Equal("ignored log content", await File.ReadAllTextAsync(logFile));
    }

    private static void RunGit(string workingDirectory, string args)
    {
        TestHelper.RunGit(workingDirectory, args);
    }
}
