using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class ReadOnlyCommandsTests
{
    [Fact]
    public async Task Status_CleanRepository_OutputsNothingToCommit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["status"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("nothing to commit, working tree clean", response.StdOut);
    }

    [Fact]
    public async Task Status_UntrackedAndModified_OutputsCorrectSections()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        File.WriteAllText(Path.Combine(testRepo.WorkingDirectory, "untracked.txt"), "hello");
        File.WriteAllText(Path.Combine(testRepo.WorkingDirectory, "README.md"), "modified content");

        var response = await emulator.InvokeAsync(["status"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Untracked files:", response.StdOut);
        Assert.Contains("untracked.txt", response.StdOut);
        Assert.Contains("Changes not staged for commit:", response.StdOut);
        Assert.Contains("README.md", response.StdOut);
    }

    [Fact]
    public async Task Log_Oneline_OutputsShortHashesAndSubjects()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Second commit", ("file2.txt", "content2"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["log", "--oneline"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Second commit", response.StdOut);
        Assert.Contains("Initial commit", response.StdOut);
    }

    [Fact]
    public async Task Log_MaxCount_LimitsCommits()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 2", ("f2.txt", "2"));
        testRepo.Commit("Commit 3", ("f3.txt", "3"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["log", "--oneline", "-n", "1"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Commit 3", response.StdOut);
        Assert.DoesNotContain("Commit 2", response.StdOut);
    }

    [Fact]
    public async Task Log_MaxCountZero_ReturnsNoCommits()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 2", ("f2.txt", "2"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["log", "--oneline", "-n", "0"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal(string.Empty, response.StdOut.Trim());
    }

    [Fact]
    public async Task Log_ZeroShorthand_ReturnsNoCommits()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 2", ("f2.txt", "2"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["log", "--oneline", "-0"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal(string.Empty, response.StdOut.Trim());
    }

    [Fact]
    public async Task Diff_Unstaged_ShowsFileDiff()
    {
        using var testRepo = GitTestRepository.Create();
        File.WriteAllText(Path.Combine(testRepo.WorkingDirectory, "README.md"), "changed seed");
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["diff"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("-seed", response.StdOut);
        Assert.Contains("+changed seed", response.StdOut);
    }

    [Fact]
    public async Task Show_RefAndPath_ShowsBlobContent()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["show", "HEAD:README.md"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("seed", response.StdOut.Trim());
    }

    [Fact]
    public async Task Show_Commit_ShowsMetadataAndDiff()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["show", "HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Initial commit", response.StdOut);
        Assert.Contains("commit ", response.StdOut);
    }

    [Fact]
    public async Task Branch_ListsBranches()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["branch"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("master", response.StdOut);
    }

    [Fact]
    public async Task RevParse_ReturnsFullHeadHash()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["rev-parse", "HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal(testRepo.Head.ToString(), response.StdOut.Trim());
    }

    [Fact]
    public async Task LsTree_ReturnsEntries()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["ls-tree", "HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("blob\tREADME.md", response.StdOut);
    }

    [Fact]
    public async Task CatFile_TypeAndPrint()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var typeResponse = await emulator.InvokeAsync(["cat-file", "-t", "HEAD"], approval);
        Assert.Equal(0, typeResponse.ExitCode);
        Assert.Equal("commit", typeResponse.StdOut.Trim());

        var printResponse = await emulator.InvokeAsync(["cat-file", "-p", "HEAD"], approval);
        Assert.Equal(0, printResponse.ExitCode);
        Assert.Contains("Initial commit", printResponse.StdOut);
    }

    [Fact]
    public async Task Config_Get_ReturnsValue()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["config", "user.name"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("Test User", response.StdOut.Trim());
    }

    [Fact]
    public async Task Remote_Verbose_WhenNoRemote_ReturnsEmptySuccess()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["remote", "-v"], approval);

        Assert.Equal(0, response.ExitCode);
    }

    [Fact]
    public async Task Config_GetOption_ReturnsValueWithoutOverwriting()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["config", "--get", "user.name"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("Test User", response.StdOut.Trim());

        // Ensure --get was not saved as a key
        var invalidKeyRes = await emulator.InvokeAsync(["config", "--get"], approval);
        Assert.NotEqual(0, invalidKeyRes.ExitCode);
    }

    [Fact]
    public async Task Log_NegativeNumberShorthand_LimitsCommits()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 2", ("f2.txt", "2"));
        testRepo.Commit("Commit 3", ("f3.txt", "3"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["log", "--oneline", "-1"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Commit 3", response.StdOut);
        Assert.DoesNotContain("Commit 2", response.StdOut);
    }

    [Fact]
    public async Task Status_Short_OutputsCompactStatus()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        File.WriteAllText(Path.Combine(testRepo.WorkingDirectory, "newfile.txt"), "hello");

        var response = await emulator.InvokeAsync(["status", "-s"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("?? newfile.txt", response.StdOut);
    }

    [Fact]
    public async Task Show_WithoutArgs_DefaultsToHead()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["show"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Initial commit", response.StdOut);
        Assert.Contains("commit ", response.StdOut);
    }

    [Fact]
    public async Task RevParse_ShowToplevel_And_GitDir()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var topLevelRes = await emulator.InvokeAsync(["rev-parse", "--show-toplevel"], approval);
        Assert.Equal(0, topLevelRes.ExitCode);
        Assert.Equal(testRepo.WorkingDirectory, topLevelRes.StdOut.Trim());

        var gitDirRes = await emulator.InvokeAsync(["rev-parse", "--git-dir"], approval);
        Assert.Equal(0, gitDirRes.ExitCode);
        Assert.Equal(Path.Combine(testRepo.WorkingDirectory, ".git"), gitDirRes.StdOut.Trim());

        var insideWorkTreeRes = await emulator.InvokeAsync(["rev-parse", "--is-inside-work-tree"], approval);
        Assert.Equal(0, insideWorkTreeRes.ExitCode);
        Assert.Equal("true", insideWorkTreeRes.StdOut.Trim());
    }

    [Fact]
    public async Task Branch_Rename_SingleArg_RenamesCurrentBranch()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var res = await emulator.InvokeAsync(["branch", "-m", "main"], approval);
        Assert.Equal(0, res.ExitCode);

        var branchListRes = await emulator.InvokeAsync(["branch"], approval);
        Assert.Contains("main", branchListRes.StdOut);
    }

    [Fact]
    public async Task RevParse_HeadTilde1_ReturnsParentHash()
    {
        using var testRepo = GitTestRepository.Create();
        var initialHash = testRepo.Head.ToString();
        testRepo.Commit("Commit 2", ("f2.txt", "c2"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["rev-parse", "HEAD~1"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal(initialHash, response.StdOut.Trim());
    }

    [Fact]
    public async Task RevParse_HeadCaret_IsSameAsHeadTilde1()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 2", ("f2.txt", "c2"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var tilde1 = await emulator.InvokeAsync(["rev-parse", "HEAD~1"], approval);
        var caret = await emulator.InvokeAsync(["rev-parse", "HEAD^"], approval);

        Assert.Equal(0, tilde1.ExitCode);
        Assert.Equal(tilde1.StdOut.Trim(), caret.StdOut.Trim());
    }

    [Fact]
    public async Task Log_WithHeadTilde1_StartsFromParent()
    {
        using var testRepo = GitTestRepository.Create();
        testRepo.Commit("Commit 2", ("f2.txt", "c2"));
        testRepo.Commit("Commit 3", ("f3.txt", "c3"));
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        // Starting log from HEAD~1 should exclude the tip commit (Commit 3)
        var response = await emulator.InvokeAsync(["log", "--oneline", "HEAD~1"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.DoesNotContain("Commit 3", response.StdOut);
        Assert.Contains("Commit 2", response.StdOut);
    }
}

