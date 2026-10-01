using Microsoft.Extensions.AI;
using Pmad.Git.CliEmulator;
using Pmad.Git.CliEmulator.AI;

namespace Pmad.Git.CliEmulator.AI.Test;

/// <summary>
/// Tests that verify the response formatting done by <see cref="GitAIFunctionFactory"/>:
/// - successful commands return stdout (or a "(success, no output)" sentinel)
/// - failed commands return a "[exit N] ..." prefix
/// </summary>
public class ResponseFormattingTests
{
    [Fact]
    public async Task SuccessWithOutput_ReturnsStdOut()
    {
        using var testRepo = GitTestRepository.Create();
        using var emulator = GitCliEmulator.Open(testRepo.WorkingDirectory);
        var fn = emulator.CreateAIFunction();

        var result = await InvokeAsync(fn, ["log", "--oneline"]);

        Assert.NotNull(result);
        Assert.DoesNotContain("[exit", result);
        Assert.Contains("Initial commit", result);
    }

    [Fact]
    public async Task SuccessWithNoOutput_ReturnsSentinel()
    {
        using var testRepo = GitTestRepository.Create();
        using var emulator = GitCliEmulator.Open(testRepo.WorkingDirectory);
        var fn = emulator.CreateAIFunction();

        // `remote -v` on a repo with no remote produces exit 0 and empty stdout
        var result = await InvokeAsync(fn, ["remote", "-v"]);

        Assert.Equal("(success, no output)", result);
    }

    [Fact]
    public async Task FailedCommand_ReturnsExitCodePrefix()
    {
        using var testRepo = GitTestRepository.Create();
        using var emulator = GitCliEmulator.Open(testRepo.WorkingDirectory);
        var fn = emulator.CreateAIFunction();

        var result = await InvokeAsync(fn, ["notacommand"]);

        Assert.StartsWith("[exit ", result);
    }

    [Fact]
    public async Task DeniedCommand_ReturnsExit130Prefix()
    {
        using var testRepo = GitTestRepository.Create();
        using var emulator = GitCliEmulator.Open(testRepo.WorkingDirectory);

        // Modify a tracked file so restore has something to do
        File.WriteAllText(Path.Combine(testRepo.WorkingDirectory, "README.md"), "modified");

        var fn = emulator.CreateAIFunction(new AlwaysDenyApproval());

        var result = await InvokeAsync(fn, ["restore", "README.md"]);

        Assert.StartsWith("[exit 130]", result);
    }

    private static async Task<string> InvokeAsync(AIFunction fn, string[] args)
    {
        var result = await fn.InvokeAsync(new AIFunctionArguments { ["args"] = args });
        return result?.ToString() ?? string.Empty;
    }
}
