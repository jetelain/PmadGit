using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer;

/// <summary>
/// Service interface for managing Git repository instances.
/// </summary>
public interface IGitRepositoryService
{
    /// <summary>
    /// Opens or retrieves a cached Git repository instance.
    /// </summary>
    /// <param name="repositoryPath">The path to the Git repository.</param>
    /// <returns>A <see cref="IGitRepository"/> instance.</returns>
    /// <exception cref="DirectoryNotFoundException">Thrown when the repository path does not exist.</exception>
    IGitRepository GetRepository(string repositoryPath);

    /// <summary>
    /// Invalidates the cache for all repositories, forcing them to be reopened on next access.
    /// </summary>
    void InvalidateCache();

    /// <summary>
    /// Invalidates the cache for a specific repository.
    /// </summary>
    /// <param name="repositoryPath">The path to the Git repository to invalidate.</param>
    void InvalidateRepository(string repositoryPath);
}
