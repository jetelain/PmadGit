using Microsoft.Extensions.AI;
using Pmad.Git.CliEmulator;
using Pmad.Git.CliEmulator.AI;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.CliEmulator.AI.Test;

/// <summary>
/// Tests for <see cref="GitCliEmulatorAIExtensions"/>: CreateAIFunction and
/// CreateAIFunctionFromString with and without an explicit approval gate.
/// </summary>
public class AIExtensionsTests
{
    // ── CreateAIFunction ───────────────────────────────────────────────────

    [Fact]
    public void CreateAIFunction_WithApproval_ReturnsFunctionNamedGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        using var emulator = new GitCliEmulator(repo);

        var fn = emulator.CreateAIFunction(new AlwaysDenyApproval());

        Assert.NotNull(fn);
        Assert.Equal("git", fn.Name);
    }

    [Fact]
    public async Task CreateAIFunction_WithDenyApproval_RestoreReturnsExit130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        using var emulator = new GitCliEmulator(repo);

        File.WriteAllText(Path.Combine(testRepo.WorkingDirectory, "README.md"), "modified");

        var fn = emulator.CreateAIFunction(new AlwaysDenyApproval());
        var result = await fn.InvokeAsync(new AIFunctionArguments { ["args"] = new[] { "restore", "README.md" } });

        Assert.StartsWith("[exit 130]", result?.ToString());
    }

    // ── CreateAIFunctionFromString ─────────────────────────────────────────

    [Fact]
    public void CreateAIFunctionFromString_WithApproval_ReturnsFunctionNamedGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        using var emulator = new GitCliEmulator(repo);

        var fn = emulator.CreateAIFunctionFromString(new AlwaysDenyApproval());

        Assert.NotNull(fn);
        Assert.Equal("git", fn.Name);
    }

    [Fact]
    public async Task CreateAIFunctionFromString_WithDenyApproval_RestoreReturnsExit130()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        using var emulator = new GitCliEmulator(repo);

        File.WriteAllText(Path.Combine(testRepo.WorkingDirectory, "README.md"), "modified");

        var fn = emulator.CreateAIFunctionFromString(new AlwaysDenyApproval());
        var result = await fn.InvokeAsync(new AIFunctionArguments { ["commandLine"] = "restore README.md" });

        Assert.StartsWith("[exit 130]", result?.ToString());
    }

    [Fact]
    public async Task CreateAIFunctionFromString_SuccessWithStdOut_ReturnsOutput()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        using var emulator = new GitCliEmulator(repo);

        var fn = emulator.CreateAIFunctionFromString();

        var result = await fn.InvokeAsync(new AIFunctionArguments { ["commandLine"] = "log --oneline" });

        Assert.Contains("Initial commit", result?.ToString());
        Assert.DoesNotContain("[exit ", result?.ToString());
    }
}
