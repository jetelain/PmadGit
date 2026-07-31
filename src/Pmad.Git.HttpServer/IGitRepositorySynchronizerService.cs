using Pmad.Git.Cli;

namespace Pmad.Git.HttpServer;

/// <summary>
/// Service interface for managing <see cref="GitRepositorySynchronizer"/> instances that keep
/// repositories managed by <see cref="IGitRepositoryService"/> synchronized with their remote.
/// </summary>
public interface IGitRepositorySynchronizerService
{
    /// <summary>
    /// Gets the cached <see cref="GitRepositorySynchronizer"/> for the repository at
    /// <paramref name="repositoryPath"/>, or <c>null</c> when none has been set up yet via
    /// <see cref="SetupSynchronizer"/>.
    /// </summary>
    /// <param name="repositoryPath">The path to the Git repository.</param>
    /// <returns>The cached <see cref="GitRepositorySynchronizer"/> instance, or <c>null</c> when none exists.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when the repository path does not exist.</exception>
    GitRepositorySynchronizer? GetSynchronizerByPath(string repositoryPath);

    /// <summary>
    /// Creates (or replaces) the cached <see cref="GitRepositorySynchronizer"/> for the repository
    /// at <paramref name="repositoryPath"/> using the given <paramref name="options"/>. If a
    /// synchronizer was already cached for this path, it is disposed and replaced.
    /// </summary>
    /// <param name="repositoryPath">The path to the Git repository.</param>
    /// <param name="options">Synchronization options used to create the synchronizer.</param>
    /// <returns>The newly created <see cref="GitRepositorySynchronizer"/> instance.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when the repository path does not exist.</exception>
    GitRepositorySynchronizer SetupSynchronizer(string repositoryPath, GitSyncOptions options);

    /// <summary>
    /// Disposes and removes the cached synchronizer for a specific repository, if any.
    /// </summary>
    /// <param name="repositoryPath">The path to the Git repository.</param>
    void InvalidateSynchronizer(string repositoryPath);
}
