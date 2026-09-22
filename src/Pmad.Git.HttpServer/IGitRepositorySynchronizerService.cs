using Pmad.Git.Cli;
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
    /// <see cref="SetupCliSynchronizer"/>.
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
    GitRepositorySynchronizer SetupCliSynchronizer(string repositoryPath, GitCliSyncOptions options);

    /// <summary>
    /// Ensures a local repository exists at <paramref name="repositoryPath"/>, cloning it from
    /// <paramref name="remoteUrl"/> first if the directory does not exist yet or is empty, then
    /// creates (or replaces) its cached <see cref="GitRepositorySynchronizer"/> using the given
    /// <paramref name="options"/> and starts synchronization, exactly as <see cref="SetupCliSynchronizer"/>.
    /// </summary>
    /// <param name="repositoryPath">The local path of the Git repository.</param>
    /// <param name="remoteUrl">URL of the remote repository to clone from, when the local repository does not exist yet.</param>
    /// <param name="options">Synchronization options used to create the synchronizer (and to clone, e.g. <see cref="GitSyncOptions.Branch"/>, <see cref="GitSyncOptions.Remote"/>).</param>
    /// <param name="cancellationToken">Token used to cancel the clone operation.</param>
    /// <returns>The newly created <see cref="GitRepositorySynchronizer"/> instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when <paramref name="repositoryPath"/> already exists, is not empty, but does not contain a git repository.</exception>
    Task<GitRepositorySynchronizer> SetupCliSynchronizerAsync(string repositoryPath, string remoteUrl, GitCliSyncOptions options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disposes and removes the cached synchronizer for a specific repository, if any.
    /// </summary>
    /// <param name="repositoryPath">The path to the Git repository.</param>
    void InvalidateSynchronizer(string repositoryPath);
}
