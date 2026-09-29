using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class ApprovalGateTests
{
    [Fact]
    public async Task Restore_WhenDenied_ReturnsExitCode130_AndDoesNotOverwrite()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval
        {
            AllowDiscardLocalChanges = false // DENIED
        };

        var readmePath = Path.Combine(testRepo.WorkingDirectory, "README.md");
        File.WriteAllText(readmePath, "modified locally");

        var response = await emulator.InvokeAsync(["restore", "README.md"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("denied", response.StdErr);
        Assert.Equal("modified locally", File.ReadAllText(readmePath));
        Assert.Single(approval.DiscardLocalChangesCalls);
        Assert.Equal("restore", approval.DiscardLocalChangesCalls[0].Operation);
    }

    [Fact]
    public async Task ResetHard_WhenDenied_ReturnsExitCode130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval
        {
            AllowDiscardLocalChanges = false // DENIED
        };

        File.WriteAllText(Path.Combine(testRepo.WorkingDirectory, "README.md"), "changed");

        var response = await emulator.InvokeAsync(["reset", "--hard", "HEAD"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("denied", response.StdErr);
    }

    [Fact]
    public async Task BranchDelete_Force_CallsApproveUnpushedCommitLoss()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval
        {
            AllowUnpushedCommitLoss = true
        };

        await emulator.InvokeAsync(["branch", "to-delete"], approval);
        var response = await emulator.InvokeAsync(["branch", "-D", "to-delete"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Single(approval.UnpushedCommitLossCalls);
        Assert.Equal("branch -D", approval.UnpushedCommitLossCalls[0].Operation);
        Assert.Equal("to-delete", approval.UnpushedCommitLossCalls[0].BranchName);
    }

    [Fact]
    public async Task BranchDelete_Force_WhenDenied_ReturnsExitCode130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        var approval = new TestUserApproval
        {
            AllowUnpushedCommitLoss = false // DENIED
        };

        await emulator.InvokeAsync(["branch", "to-delete-denied"], approval);
        var response = await emulator.InvokeAsync(["branch", "-D", "to-delete-denied"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("denied", response.StdErr);

        var listRes = await emulator.InvokeAsync(["branch"], approval);
        Assert.Contains("to-delete-denied", listRes.StdOut);
    }

    [Fact]
    public async Task Fetch_WhenRemoteNotConfigured_Returns128()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo, remote: null);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["fetch"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("No remote configured", response.StdErr);
        Assert.Empty(approval.ReadRemoteCalls);
    }

    [Fact]
    public async Task Pull_WhenRemoteNotConfigured_Returns128()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo, remote: null);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["pull"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("No remote configured", response.StdErr);
        Assert.Empty(approval.ReadRemoteCalls);
    }

    [Fact]
    public async Task Push_WhenRemoteNotConfigured_Returns128()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo, remote: null);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["push"], approval);

        Assert.Equal(128, response.ExitCode);
        Assert.Contains("No remote configured", response.StdErr);
        Assert.Empty(approval.WriteRemoteCalls);
    }

    [Fact]
    public async Task Cancellation_ReturnsExitCode130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var approval = new TestUserApproval
        {
            CancellationToken = cts.Token
        };

        var response = await emulator.InvokeAsync(["status"], approval);

        Assert.Equal(130, response.ExitCode);
        Assert.Contains("cancelled", response.StdErr);
    }
}
