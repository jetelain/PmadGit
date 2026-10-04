using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class RevParseCommandTests
{
    [Fact]
    public async Task RevParse_AbbrevRef_HEAD_OnBranch_ReturnsBranchName()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["rev-parse", "--abbrev-ref", "HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("master", response.StdOut.Trim());
    }

    [Fact]
    public async Task RevParse_AbbrevRef_HEAD_Detached_ReturnsHEAD()
    {
        using var testRepo = GitTestRepository.Create();
        var headHash = testRepo.Head.ToString();
        testRepo.RunGit($"checkout --detach {headHash}");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["rev-parse", "--abbrev-ref", "HEAD"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("HEAD", response.StdOut.Trim());
    }

    [Fact]
    public async Task RevParse_AbbrevRef_NoRefSpecified_ReturnsFatal()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["rev-parse", "--abbrev-ref"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("fatal: Reference required.", response.StdErr);
    }

    [Theory]
    [InlineData("@{u}")]
    [InlineData("@{upstream}")]
    [InlineData("HEAD@{u}")]
    [InlineData("HEAD@{upstream}")]
    public async Task RevParse_AbbrevRef_Upstream_WhenNoUpstreamConfigured_ReturnsFatal(string upstreamRef)
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["rev-parse", "--abbrev-ref", upstreamRef], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("No upstream configured for branch 'master'.", response.StdErr);
    }

    [Fact]
    public async Task RevParse_AbbrevRef_Upstream_WhenDetached_ReturnsFatal()
    {
        using var testRepo = GitTestRepository.Create();
        var headHash = testRepo.Head.ToString();
        testRepo.RunGit($"checkout --detach {headHash}");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["rev-parse", "--abbrev-ref", "@{u}"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("HEAD does not point to a branch.", response.StdErr);
    }

    [Fact]
    public async Task RevParse_AbbrevRef_Upstream_WhenConfigured_ReturnsUpstreamBranch()
    {
        using var testRepo = GitTestRepository.Create();
        var configPath = Path.Combine(testRepo.WorkingDirectory, ".git", "config");
        File.AppendAllText(configPath, "\n[branch \"master\"]\n\tremote = origin\n\tmerge = refs/heads/master\n");
        testRepo.RunGit($"update-ref refs/remotes/origin/master {testRepo.Head}");

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["rev-parse", "--abbrev-ref", "@{u}"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("origin/master", response.StdOut.Trim());
    }

    [Fact]
    public async Task RevParse_AbbrevRef_RefsHeadsPrefix_StripsPrefix()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["rev-parse", "--abbrev-ref", "refs/heads/feature/login"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("feature/login", response.StdOut.Trim());
    }

    [Fact]
    public async Task RevParse_AbbrevRef_OtherRef_ReturnsAsIs()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["rev-parse", "--abbrev-ref", "origin/master"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("origin/master", response.StdOut.Trim());
    }

    [Fact]
    public async Task RevParse_ShowToplevel_OutputsRootPath()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["rev-parse", "--show-toplevel"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal(repo.RootPath, response.StdOut.Trim());
    }

    [Fact]
    public async Task RevParse_GitDir_OutputsGitDirectory()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["rev-parse", "--git-dir"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal(repo.GitDirectory, response.StdOut.Trim());
    }

    [Fact]
    public async Task RevParse_IsInsideWorkTree_OutputsTrue()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["rev-parse", "--is-inside-work-tree"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Equal("true", response.StdOut.Trim());
    }

    [Fact]
    public async Task RevParse_NoArguments_ReturnsFatal()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["rev-parse"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("fatal: Reference required.", response.StdErr);
    }

    [Fact]
    public async Task RevParse_ValidRef_OutputsCommitHash()
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
    public async Task RevParse_InvalidRef_ReturnsFatal()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["rev-parse", "non-existent-ref-12345"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("fatal:", response.StdErr);
    }
}
