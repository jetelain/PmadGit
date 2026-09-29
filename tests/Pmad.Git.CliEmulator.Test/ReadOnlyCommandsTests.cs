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
}
