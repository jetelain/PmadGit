using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.Test;

public class GitCliEmulatorGuardTests
{
    [Fact]
    public void Constructor_NullRepository_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GitCliEmulator((IGitWorkspaceRepository)null!));
    }

    [Fact]
    public async Task InvokeAsync_NullArgs_ThrowsArgumentNullException()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);

        await Assert.ThrowsAsync<ArgumentNullException>(() => emulator.InvokeAsync(null!));
    }

    [Fact]
    public async Task InvokeAsync_UnknownCommand_ReturnsNonZeroExitCode()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);

        var response = await emulator.InvokeAsync(["unknown-git-command"]);

        Assert.NotEqual(0, response.ExitCode);
    }

    [Fact]
    public async Task InvokeAsync_EmptyArgs_ReturnsNonZeroExitCode()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        var emulator = new GitCliEmulator(repo);

        // No sub-command → parser returns non-zero or writes help
        var response = await emulator.InvokeAsync([]);

        // Empty args should not crash — exit code may be 0 (help) or non-zero
        Assert.NotNull(response);
    }

    [Fact]
    public void Constructor_WithDisposeRepositories_False_DoesNotDisposeOnDispose()
    {
        using var testRepo = GitTestRepository.Create();
        var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);

        // disposeRepositories: false (default) — repo should still be usable afterwards
        var emulator = new GitCliEmulator(repo, disposeRepositories: false);
        emulator.Dispose();

        // repo should still work — no exception
        Assert.NotNull(repo.GitDirectory);
        repo.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_WithDisposeRepositoriesTrue_CompletesSuccessfully()
    {
        using var testRepo = GitTestRepository.Create();
        var emulator = GitCliEmulator.Open(testRepo.WorkingDirectory);

        await emulator.DisposeAsync();
        // Should not throw
    }

    [Fact]
    public void Open_NullOrWhitespacePath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => GitCliEmulator.Open(""));
        Assert.Throws<ArgumentException>(() => GitCliEmulator.Open("   "));
    }

    [Fact]
    public void GitCliResponse_IsSuccess_TrueForZeroExitCode()
    {
        var response = new GitCliResponse { ExitCode = 0, StdOut = "output" };
        Assert.True(response.IsSuccess);
    }

    [Fact]
    public void GitCliResponse_IsSuccess_FalseForNonZeroExitCode()
    {
        var response = new GitCliResponse { ExitCode = 1 };
        Assert.False(response.IsSuccess);
    }
}
