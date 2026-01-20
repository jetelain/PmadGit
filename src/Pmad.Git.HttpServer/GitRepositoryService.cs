using System.Collections.Concurrent;
using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer;

/// <summary>
/// Default implementation of <see cref="IGitRepositoryService"/> that maintains a cache of repository instances.
/// This service is thread-safe and should be registered as a singleton.
/// </summary>
internal sealed class GitRepositoryService : IGitRepositoryService
{
    private readonly ConcurrentDictionary<string, GitRepository> _repositories = new(StringComparer.OrdinalIgnoreCase);

    public GitRepository GetRepository(string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            throw new ArgumentException("Repository path cannot be null or whitespace.", nameof(repositoryPath));
        }

        var normalizedPath = Path.GetFullPath(repositoryPath);

        if (!Directory.Exists(normalizedPath))
        {
            throw new DirectoryNotFoundException($"Repository not found at path: {normalizedPath}");
        }

        return _repositories.GetOrAdd(normalizedPath, path =>
        {
            return GitRepository.Open(path);
        });
    }

    public void InvalidateCache()
    {
        _repositories.Clear();
    }

    public void InvalidateRepository(string repositoryPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath))
        {
            return;
        }

        var normalizedPath = Path.GetFullPath(repositoryPath);
        _repositories.TryRemove(normalizedPath, out _);
    }
}
