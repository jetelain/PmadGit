using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Pmad.Git.CliEmulator;
using Pmad.Git.RemoteClient;

namespace Pmad.Git.CliEmulator.AI;

/// <summary>
/// Extension methods for <see cref="IServiceCollection"/> to register the Git CLI emulator
/// as an <see cref="AIFunction"/> in an application's dependency-injection container.
/// </summary>
public static class GitCliEmulatorAIServiceCollectionExtensions
{
    /// <summary>
    /// Registers a singleton <see cref="IGitCliEmulator"/> opened from <paramref name="repositoryPath"/>
    /// and a singleton <see cref="AIFunction"/> (named <c>git</c>) backed by that emulator.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <param name="repositoryPath">Path to the working directory or <c>.git</c> directory.</param>
    /// <param name="remoteOptions">Optional HTTP / credentials options for remote operations.</param>
    /// <param name="userApproval">
    /// Optional approval gate factory.  When <see langword="null"/> all operations are auto-approved.
    /// Receives the <see cref="IServiceProvider"/> so you can resolve scoped services if needed.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddGitCliEmulatorAI(
        this IServiceCollection services,
        string repositoryPath,
        GitRemoteClientOptions? remoteOptions = null,
        Func<IServiceProvider, IUserApproval>? userApproval = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryPath);

        services.AddSingleton<IGitCliEmulator>(_ =>
            GitCliEmulator.Open(repositoryPath, remoteOptions));

        services.AddSingleton<AIFunction>(sp =>
        {
            var emulator = sp.GetRequiredService<IGitCliEmulator>();
            var approval = userApproval?.Invoke(sp);
            return GitAIFunctionFactory.Create(emulator, approval);
        });

        return services;
    }
}
