using Pmad.Git.CliEmulator.Test.Fakes;
using Pmad.Git.LocalRepositories;
using Pmad.Git.RemoteClient;

namespace Pmad.Git.CliEmulator.Test;

public class InstantiationTests
{
    [Fact]
    public async Task Open_CreatesWorkingEmulatorWithRemote_AndDisposesUnderlyingRepositories()
    {
        using var testRepo = GitTestRepository.Create();
        var approval = new TestUserApproval();

        GitCliEmulator emulator;
        using (emulator = GitCliEmulator.Open(testRepo.WorkingDirectory))
        {
            Assert.NotNull(emulator.Repository);
            Assert.NotNull(emulator.Remote);

            var response = await emulator.InvokeAsync(["status"], approval);
            Assert.Equal(0, response.ExitCode);
            Assert.Contains("nothing to commit, working tree clean", response.StdOut);
        }

        // Disposal completed cleanly
        Assert.NotNull(emulator);
    }

    [Fact]
    public async Task Open_AsyncDispose_DisposesUnderlyingRepositories()
    {
        using var testRepo = GitTestRepository.Create();
        var approval = new TestUserApproval();

        GitCliEmulator emulator;
        await using (emulator = GitCliEmulator.Open(testRepo.WorkingDirectory))
        {
            var response = await emulator.InvokeAsync(["rev-parse", "HEAD"], approval);
            Assert.Equal(0, response.ExitCode);
            Assert.Equal(testRepo.Head.ToString(), response.StdOut.Trim());
        }

        Assert.NotNull(emulator);
    }

    [Fact]
    public async Task Constructor_FromRemoteClientRepository_Works()
    {
        using var testRepo = GitTestRepository.Create();
        using var localRepo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        using var remoteRepo = new GitRemoteClientRepository(localRepo);
        var emulator = new GitCliEmulator(remoteRepo);
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["branch"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("master", response.StdOut);
        Assert.Same(localRepo, emulator.Repository);
        Assert.Same(remoteRepo, emulator.Remote);
    }

    [Fact]
    public async Task Extension_CreateCliEmulator_OnWorkspaceRepository_Works()
    {
        using var testRepo = GitTestRepository.Create();
        using var localRepo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = localRepo.CreateCliEmulator();
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["status"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Same(localRepo, emulator.Repository);
        Assert.NotNull(emulator.Remote);
    }

    [Fact]
    public async Task Extension_CreateCliEmulator_OnRemoteClientRepository_Works()
    {
        using var testRepo = GitTestRepository.Create();
        using var localRepo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        using var remoteRepo = new GitRemoteClientRepository(localRepo);
        var emulator = remoteRepo.CreateCliEmulator();
        var approval = new TestUserApproval();

        var response = await emulator.InvokeAsync(["log", "--oneline"], approval);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("Initial commit", response.StdOut);
    }
}
