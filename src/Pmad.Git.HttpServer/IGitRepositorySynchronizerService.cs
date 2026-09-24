using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer;

/// <summary>
/// Service interface for managing <see cref="GitRepositorySynchronizer"/> instances that keep
/// repositories managed by <see cref="IGitRepositoryService"/> synchronized with their remote.
/// </summary>
public interface IGitRepositorySynchronizerService : IAsyncDisposable
{
    /// <summary>
    /// Gets the cached <see cref="GitRepositorySynchronizer"/> for the repository at
    /// <paramref name="repositoryPath"/>, or <c>null</c> when none has been set up yet via
    /// <see cref="SetupSynchronizer(string, Func{IGitRepository, GitRepositorySynchronizer})"/>.
    /// </summary>
    /// <param name="repositoryPath">The path to the Git repository.</param>
    /// <returns>The cached <see cref="GitRepositorySynchronizer"/> instance, or <c>null</c> when none exists.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when the repository path does not exist.</exception>
    GitRepositorySynchronizer? GetSynchronizerByPath(string repositoryPath);

    /// <summary>
    /// Creates (or replaces) the cached <see cref="GitRepositorySynchronizer"/> for the repository
    /// at <paramref name="repositoryPath"/> using a factory that receives the server's managed
    /// <see cref="IGitRepository"/> instance to construct the <see cref="GitRepositorySynchronizer"/>.
    /// If a synchronizer was already cached for this path, it is disposed and replaced.
    /// </summary>
    /// <param name="repositoryPath">The path to the Git repository.</param>
    /// <param name="synchronizerFactory">Factory creating the <see cref="GitRepositorySynchronizer"/> using the managed <see cref="IGitRepository"/>.</param>
    /// <returns>The newly created <see cref="GitRepositorySynchronizer"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="synchronizerFactory"/> is null.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the repository path does not exist.</exception>
    GitRepositorySynchronizer SetupSynchronizer(string repositoryPath, Func<IGitRepository, GitRepositorySynchronizer> synchronizerFactory);

    /// <summary>
    /// Creates (or replaces) the cached <see cref="GitRepositorySynchronizer"/> for the repository
    /// at <paramref name="repositoryPath"/> using a factory that receives the server's managed
    /// <see cref="IGitRepository"/> instance to construct an <see cref="IGitRepositoryWithRemote"/>,
    /// then starts synchronization. If a synchronizer was already cached for this path, it is disposed and replaced.
    /// </summary>
    /// <param name="repositoryPath">The path to the Git repository.</param>
    /// <param name="remoteRepositoryFactory">Factory creating the remote repository using the managed <see cref="IGitRepository"/>.</param>
    /// <param name="options">Optional synchronization options.</param>
    /// <returns>The newly created <see cref="GitRepositorySynchronizer"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="remoteRepositoryFactory"/> is null.</exception>
    /// <exception cref="DirectoryNotFoundException">Thrown when the repository path does not exist.</exception>
    GitRepositorySynchronizer SetupSynchronizer(string repositoryPath, Func<IGitRepository, IGitRepositoryWithRemote> remoteRepositoryFactory, GitSyncOptions? options = null);

    /// <summary>
    /// Ensures a local repository exists at <paramref name="repositoryPath"/>, invoking <paramref name="cloneAsync"/>
    /// if the directory does not exist yet or is empty, then creates (or replaces) its cached
    /// <see cref="GitRepositorySynchronizer"/> using the given <paramref name="synchronizerFactory"/>.
    /// </summary>
    /// <param name="repositoryPath">The local path of the Git repository.</param>
    /// <param name="cloneAsync">Asynchronous action to clone the repository into the given path if it does not exist yet.</param>
    /// <param name="synchronizerFactory">Factory creating the <see cref="GitRepositorySynchronizer"/> using the managed <see cref="IGitRepository"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created <see cref="GitRepositorySynchronizer"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="cloneAsync"/> or <paramref name="synchronizerFactory"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="repositoryPath"/> already exists, is not empty, but does not contain a git repository.</exception>
    Task<GitRepositorySynchronizer> SetupSynchronizerAsync(
        string repositoryPath,
        Func<string, CancellationToken, Task> cloneAsync,
        Func<IGitRepository, GitRepositorySynchronizer> synchronizerFactory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a local repository exists at <paramref name="repositoryPath"/>, invoking <paramref name="cloneAsync"/>
    /// if the directory does not exist yet or is empty, then creates (or replaces) its cached
    /// <see cref="GitRepositorySynchronizer"/> using the given <paramref name="remoteRepositoryFactory"/>.
    /// </summary>
    /// <param name="repositoryPath">The local path of the Git repository.</param>
    /// <param name="cloneAsync">Asynchronous action to clone the repository into the given path if it does not exist yet.</param>
    /// <param name="remoteRepositoryFactory">Factory creating the remote repository using the managed <see cref="IGitRepository"/>.</param>
    /// <param name="options">Optional synchronization options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The newly created <see cref="GitRepositorySynchronizer"/> instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="cloneAsync"/> or <paramref name="remoteRepositoryFactory"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="repositoryPath"/> already exists, is not empty, but does not contain a git repository.</exception>
    Task<GitRepositorySynchronizer> SetupSynchronizerAsync(
        string repositoryPath,
        Func<string, CancellationToken, Task> cloneAsync,
        Func<IGitRepository, IGitRepositoryWithRemote> remoteRepositoryFactory,
        GitSyncOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes and removes the cached synchronizer for a specific repository, if any.
    /// </summary>
    /// <param name="repositoryPath">The path to the Git repository.</param>
    void InvalidateSynchronizer(string repositoryPath);
}
