using Pmad.Git.CliEmulator;
using Pmad.Git.CliEmulator.AI;
using Pmad.Git.LocalRepositories;
using Microsoft.Extensions.AI;

namespace Pmad.Git.CliEmulator.AI.Test;

/// <summary>
/// Tests that verify the three ways a caller can obtain an AIFunction:
/// - factory (array-args variant)
/// - factory (string-args variant)
/// - extension method on the emulator
/// </summary>
public class GitAIFunctionFactoryTests
{
    // ── Factory: array-args ────────────────────────────────────────────────

    [Fact]
    public void Create_ReturnsAIFunction_Named_git()
    {
        using var testRepo = GitTestRepository.Create();
        using var emulator = GitCliEmulator.Open(testRepo.WorkingDirectory);

        var fn = GitAIFunctionFactory.Create(emulator);

        Assert.NotNull(fn);
        Assert.Equal("git", fn.Name);
    }

    [Fact]
    public async Task Create_Status_CleanRepo_ReturnsSuccessOutput()
    {
        using var testRepo = GitTestRepository.Create();
        using var emulator = GitCliEmulator.Open(testRepo.WorkingDirectory);
        var fn = GitAIFunctionFactory.Create(emulator);

        var result = await InvokeWithArgsAsync(fn, ["status"]);

        Assert.Contains("nothing to commit", result);
    }

    [Fact]
    public async Task Create_Log_Oneline_ReturnsCommitLine()
    {
        using var testRepo = GitTestRepository.Create();
        using var emulator = GitCliEmulator.Open(testRepo.WorkingDirectory);
        var fn = GitAIFunctionFactory.Create(emulator);

        var result = await InvokeWithArgsAsync(fn, ["log", "--oneline"]);

        Assert.Contains("Initial commit", result);
    }

    [Fact]
    public async Task Create_UnknownCommand_ReturnsExitCodePrefix()
    {
        using var testRepo = GitTestRepository.Create();
        using var emulator = GitCliEmulator.Open(testRepo.WorkingDirectory);
        var fn = GitAIFunctionFactory.Create(emulator);

        var result = await InvokeWithArgsAsync(fn, ["notacommand"]);

        Assert.StartsWith("[exit ", result);
    }

    // ── Factory: string-args ───────────────────────────────────────────────

    [Fact]
    public void CreateFromString_ReturnsAIFunction_Named_git()
    {
        using var testRepo = GitTestRepository.Create();
        using var emulator = GitCliEmulator.Open(testRepo.WorkingDirectory);

        var fn = GitAIFunctionFactory.CreateFromString(emulator);

        Assert.NotNull(fn);
        Assert.Equal("git", fn.Name);
    }

    [Fact]
    public async Task CreateFromString_Status_ReturnsSuccessOutput()
    {
        using var testRepo = GitTestRepository.Create();
        using var emulator = GitCliEmulator.Open(testRepo.WorkingDirectory);
        var fn = GitAIFunctionFactory.CreateFromString(emulator);

        var result = await InvokeWithStringAsync(fn, "status");

        Assert.Contains("nothing to commit", result);
    }

    [Fact]
    public async Task CreateFromString_LogOneline_ReturnsCommitLine()
    {
        using var testRepo = GitTestRepository.Create();
        using var emulator = GitCliEmulator.Open(testRepo.WorkingDirectory);
        var fn = GitAIFunctionFactory.CreateFromString(emulator);

        var result = await InvokeWithStringAsync(fn, "log --oneline");

        Assert.Contains("Initial commit", result);
    }

    [Fact]
    public async Task CreateFromString_CommitWithQuotedMessage_ParsesQuotesCorrectly()
    {
        using var testRepo = GitTestRepository.Create();
        using var emulator = GitCliEmulator.Open(testRepo.WorkingDirectory);
        var fn = GitAIFunctionFactory.CreateFromString(emulator);

        // Stage a new file first
        File.WriteAllText(Path.Combine(testRepo.WorkingDirectory, "new.txt"), "content");
        await InvokeWithStringAsync(fn, "add new.txt");

        var result = await InvokeWithStringAsync(fn, "commit -m 'Add new file'");

        Assert.Contains("new file", result.ToLowerInvariant());
    }

    [Fact]
    public async Task CreateFromString_LogNegativeNumber_Works()
    {
        using var testRepo = GitTestRepository.Create();
        using var emulator = GitCliEmulator.Open(testRepo.WorkingDirectory);
        var fn = GitAIFunctionFactory.CreateFromString(emulator);

        var result = await InvokeWithStringAsync(fn, "log --oneline -10");

        Assert.Contains("Initial commit", result);
    }

    [Fact]
    public async Task CreateFromString_EmptyQuotedArgument_Preserved()
    {
        using var testRepo = GitTestRepository.Create();
        using var emulator = GitCliEmulator.Open(testRepo.WorkingDirectory);
        var fn = GitAIFunctionFactory.CreateFromString(emulator);

        // Setting a config key to empty string
        var result = await InvokeWithStringAsync(fn, "config test.key \"\"");

        Assert.DoesNotContain("[exit ", result);

        var getResult = await InvokeWithStringAsync(fn, "config --get test.key");
        Assert.Equal("(success, no output)", getResult.Trim());
    }

    // ── Extension methods ──────────────────────────────────────────────────

    [Fact]
    public void CreateAIFunction_ExtensionMethod_ReturnsFunctionNamedGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        using var emulator = new GitCliEmulator(repo);

        var fn = emulator.CreateAIFunction();

        Assert.NotNull(fn);
        Assert.Equal("git", fn.Name);
    }

    [Fact]
    public async Task CreateAIFunction_ExtensionMethod_Status_Works()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        using var emulator = new GitCliEmulator(repo);
        var fn = emulator.CreateAIFunction();

        var result = await InvokeWithArgsAsync(fn, ["status"]);

        Assert.Contains("nothing to commit", result);
    }

    [Fact]
    public void CreateAIFunctionFromString_ExtensionMethod_ReturnsFunctionNamedGit()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        using var emulator = new GitCliEmulator(repo);

        var fn = emulator.CreateAIFunctionFromString();

        Assert.NotNull(fn);
        Assert.Equal("git", fn.Name);
    }

    [Fact]
    public async Task CreateAIFunctionFromString_ExtensionMethod_Status_Works()
    {
        using var testRepo = GitTestRepository.Create();
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(testRepo.WorkingDirectory);
        using var emulator = new GitCliEmulator(repo);
        var fn = emulator.CreateAIFunctionFromString();

        var result = await InvokeWithStringAsync(fn, "status");

        Assert.Contains("nothing to commit", result);
    }

    // ── Null-guard ─────────────────────────────────────────────────────────

    [Fact]
    public void Create_NullEmulator_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => GitAIFunctionFactory.Create(null!));
    }

    [Fact]
    public void CreateFromString_NullEmulator_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => GitAIFunctionFactory.CreateFromString(null!));
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static async Task<string> InvokeWithArgsAsync(AIFunction fn, string[] args)
    {
        var argsList = new AIFunctionArguments
        {
            ["args"] = args
        };
        var result = await fn.InvokeAsync(argsList);
        return result?.ToString() ?? string.Empty;
    }

    private static async Task<string> InvokeWithStringAsync(AIFunction fn, string command)
    {
        var argsList = new AIFunctionArguments
        {
            ["command"] = command
        };
        var result = await fn.InvokeAsync(argsList);
        return result?.ToString() ?? string.Empty;
    }
}
