using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Pmad.Git.CliEmulator;
using Pmad.Git.CliEmulator.AI;

namespace Pmad.Git.CliEmulator.AI.Test;

/// <summary>
/// Tests for <see cref="GitCliEmulatorAIServiceCollectionExtensions.AddGitCliEmulatorAI"/>.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddGitCliEmulatorAI_RegistersIGitCliEmulator_Singleton()
    {
        using var testRepo = GitTestRepository.Create();
        var services = new ServiceCollection();

        services.AddGitCliEmulatorAI(testRepo.WorkingDirectory);

        using var sp = services.BuildServiceProvider();
        var emulator1 = sp.GetRequiredService<IGitCliEmulator>();
        var emulator2 = sp.GetRequiredService<IGitCliEmulator>();

        Assert.NotNull(emulator1);
        Assert.Same(emulator1, emulator2); // singleton
    }

    [Fact]
    public void AddGitCliEmulatorAI_RegistersAIFunction_Singleton()
    {
        using var testRepo = GitTestRepository.Create();
        var services = new ServiceCollection();

        services.AddGitCliEmulatorAI(testRepo.WorkingDirectory);

        using var sp = services.BuildServiceProvider();
        var fn1 = sp.GetRequiredService<AIFunction>();
        var fn2 = sp.GetRequiredService<AIFunction>();

        Assert.NotNull(fn1);
        Assert.Equal("git", fn1.Name);
        Assert.Same(fn1, fn2); // singleton
    }

    [Fact]
    public async Task AddGitCliEmulatorAI_AIFunction_Status_Works()
    {
        using var testRepo = GitTestRepository.Create();
        var services = new ServiceCollection();
        services.AddGitCliEmulatorAI(testRepo.WorkingDirectory);

        using var sp = services.BuildServiceProvider();
        var fn = sp.GetRequiredService<AIFunction>();

        var result = await fn.InvokeAsync(new AIFunctionArguments { ["args"] = new[] { "status" } });

        Assert.Contains("nothing to commit", result?.ToString());
    }

    [Fact]
    public void AddGitCliEmulatorAI_WithCustomApproval_UsesItForEmulator()
    {
        using var testRepo = GitTestRepository.Create();
        var services = new ServiceCollection();
        var approvalUsed = false;

        services.AddGitCliEmulatorAI(
            testRepo.WorkingDirectory,
            userApproval: _ =>
            {
                approvalUsed = true;
                return new AlwaysDenyApproval();
            });

        using var sp = services.BuildServiceProvider();
        // Resolving the AIFunction triggers creation of the emulator and the approval factory.
        var fn = sp.GetRequiredService<AIFunction>();

        Assert.True(approvalUsed);
        Assert.NotNull(fn);
    }

    [Fact]
    public void AddGitCliEmulatorAI_NullPath_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddGitCliEmulatorAI(null!));
    }

    [Fact]
    public void AddGitCliEmulatorAI_EmptyPath_ThrowsArgumentException()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddGitCliEmulatorAI("   "));
    }

    [Fact]
    public void AddGitCliEmulatorAI_NullServices_ThrowsArgumentNullException()
    {
        using var testRepo = GitTestRepository.Create();

        Assert.Throws<ArgumentNullException>(() =>
            ((IServiceCollection)null!).AddGitCliEmulatorAI(testRepo.WorkingDirectory));
    }

    [Fact]
    public void AddGitCliEmulatorAI_ReturnsServices_ForChaining()
    {
        using var testRepo = GitTestRepository.Create();
        var services = new ServiceCollection();

        var returned = services.AddGitCliEmulatorAI(testRepo.WorkingDirectory);

        Assert.Same(services, returned);
    }
}
