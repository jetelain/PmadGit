using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class MutationCommandsTests
{
    [Fact]
    public async Task Add_And_Commit_CreatesNewCommit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var newFilePath = Path.Combine(testRepo.WorkingDirectory, "newfile.txt");
        File.WriteAllText(newFilePath, "contents");

        var addResponse = await emulator.InvokeAsync(["add", "newfile.txt"], approval);
        Assert.Equal(0, addResponse.ExitCode);

        var commitResponse = await emulator.InvokeAsync(["commit", "-m", "Add new file"], approval);
        Assert.Equal(0, commitResponse.ExitCode);
        Assert.Contains("Add new file", commitResponse.StdOut);

        var logResponse = await emulator.InvokeAsync(["log", "--oneline", "-n", "1"], approval);
        Assert.Contains("Add new file", logResponse.StdOut);
    }

    [Fact]
    public async Task Commit_Amend_Unpushed_DoesNotRequireApproval()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var newFilePath = Path.Combine(testRepo.WorkingDirectory, "newfile.txt");
        File.WriteAllText(newFilePath, "contents");
        await emulator.InvokeAsync(["add", "newfile.txt"], approval);
        await emulator.InvokeAsync(["commit", "-m", "Initial new file"], approval);

        var amendResponse = await emulator.InvokeAsync(["commit", "--amend", "-m", "Amended new file"], approval);

        Assert.Equal(0, amendResponse.ExitCode);
        Assert.Contains("Amended new file", amendResponse.StdOut);
        Assert.Empty(approval.HistoryRewriteCalls);
    }

    [Fact]
    public async Task Restore_Staged_UnstagesFileWithoutApproval()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var newFilePath = Path.Combine(testRepo.WorkingDirectory, "staged.txt");
        File.WriteAllText(newFilePath, "content");
        await emulator.InvokeAsync(["add", "staged.txt"], approval);

        var statusBefore = await emulator.InvokeAsync(["status"], approval);
        Assert.Contains("Changes to be committed:", statusBefore.StdOut);

        var restoreResponse = await emulator.InvokeAsync(["restore", "--staged", "staged.txt"], approval);
        Assert.Equal(0, restoreResponse.ExitCode);
        Assert.Empty(approval.DiscardLocalChangesCalls);

        var statusAfter = await emulator.InvokeAsync(["status"], approval);
        Assert.DoesNotContain("Changes to be committed:", statusAfter.StdOut);
        Assert.Contains("Untracked files:", statusAfter.StdOut);
    }

    [Fact]
    public async Task Restore_Worktree_RequiresApproval_AndRestoresContent()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var readmePath = Path.Combine(testRepo.WorkingDirectory, "README.md");
        File.WriteAllText(readmePath, "overwritten content");

        var response = await emulator.InvokeAsync(["restore", "README.md"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Contains("README.md", approval.DiscardLocalChangesCalls[0].AffectedFiles);
        Assert.Equal("seed", File.ReadAllText(readmePath));
    }

    [Fact]
    public async Task Branch_Create_And_Rename()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var createRes = await emulator.InvokeAsync(["branch", "feat-1"], approval);
        Assert.Equal(0, createRes.ExitCode);

        var renameRes = await emulator.InvokeAsync(["branch", "-m", "feat-1", "feat-renamed"], approval);
        Assert.Equal(0, renameRes.ExitCode);

        var listRes = await emulator.InvokeAsync(["branch"], approval);
        Assert.Contains("feat-renamed", listRes.StdOut);
        Assert.DoesNotContain("feat-1", listRes.StdOut);
    }

    [Fact]
    public async Task Tag_Create_And_Delete()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var createTag = await emulator.InvokeAsync(["tag", "v1.0"], approval);
        Assert.Equal(0, createTag.ExitCode);

        var listTag = await emulator.InvokeAsync(["tag"], approval);
        Assert.Contains("v1.0", listTag.StdOut);

        var deleteTag = await emulator.InvokeAsync(["tag", "-d", "v1.0"], approval);
        Assert.Equal(0, deleteTag.ExitCode);

        var listTag2 = await emulator.InvokeAsync(["tag"], approval);
        Assert.DoesNotContain("v1.0", listTag2.StdOut);
    }

    [Fact]
    public async Task Config_Set_And_Unset()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var setRes = await emulator.InvokeAsync(["config", "custom.testkey", "testval"], approval);
        Assert.Equal(0, setRes.ExitCode);

        var getRes = await emulator.InvokeAsync(["config", "custom.testkey"], approval);
        Assert.Equal("testval", getRes.StdOut.Trim());

        var unsetRes = await emulator.InvokeAsync(["config", "--unset", "custom.testkey"], approval);
        Assert.Equal(0, unsetRes.ExitCode);
    }
}
