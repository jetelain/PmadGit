using Pmad.Git.LocalRepositories;

namespace Pmad.Git.HttpServer.Test;

public sealed class GitRepositoryServiceTest : IDisposable
{
    private readonly string _testRoot;

    public GitRepositoryServiceTest()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "PmadGitRepositoryServiceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [Fact]
    public void GetRepositoryByPath_WithValidPath_ShouldReturnRepository()
    {
        // Arrange
        var repoPath = CreateBareRepository();
        var service = new GitRepositoryService();

        // Act
        var repository = service.GetRepositoryByPath(repoPath);

        // Assert
        Assert.NotNull(repository);
        Assert.Equal(repoPath, repository.GitDirectory);
    }

    [Fact]
    public void GetRepositoryByPath_CalledTwice_ShouldReturnSameInstance()
    {
        // Arrange
        var repoPath = CreateBareRepository();
        var service = new GitRepositoryService();

        // Act
        var repository1 = service.GetRepositoryByPath(repoPath);
        var repository2 = service.GetRepositoryByPath(repoPath);

        // Assert
        Assert.Same(repository1, repository2);
    }

    [Fact]
    public void GetRepositoryByPath_WithDifferentPaths_ShouldReturnDifferentInstances()
    {
        // Arrange
        var repoPath1 = CreateBareRepository("repo1");
        var repoPath2 = CreateBareRepository("repo2");
        var service = new GitRepositoryService();

        // Act
        var repository1 = service.GetRepositoryByPath(repoPath1);
        var repository2 = service.GetRepositoryByPath(repoPath2);

        // Assert
        Assert.NotSame(repository1, repository2);
    }

    [Fact]
    public void GetRepositoryByPath_WithNullPath_ShouldThrowArgumentException()
    {
        // Arrange
        var service = new GitRepositoryService();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => service.GetRepositoryByPath(null!));
    }

    [Fact]
    public void GetRepositoryByPath_WithEmptyPath_ShouldThrowArgumentException()
    {
        // Arrange
        var service = new GitRepositoryService();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => service.GetRepositoryByPath(""));
    }

    [Fact]
    public void GetRepositoryByPath_WithWhitespacePath_ShouldThrowArgumentException()
    {
        // Arrange
        var service = new GitRepositoryService();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => service.GetRepositoryByPath("   "));
    }

    [Fact]
    public void GetRepositoryByPath_WithNonExistentPath_ShouldThrowDirectoryNotFoundException()
    {
        // Arrange
        var service = new GitRepositoryService();
        var nonExistentPath = Path.Combine(_testRoot, "non-existent");

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => service.GetRepositoryByPath(nonExistentPath));
    }

    [Fact]
    public void GetRepositoryByPath_WithNormalizedPaths_ShouldReturnSameInstance()
    {
        // Arrange
        var repoPath = CreateBareRepository();
        var service = new GitRepositoryService();
        
        // Different representations of the same path
        var path1 = repoPath;
        var path2 = repoPath.TrimEnd(Path.DirectorySeparatorChar);
        var path3 = Path.GetFullPath(repoPath);

        // Act
        var repository1 = service.GetRepositoryByPath(path1);
        var repository2 = service.GetRepositoryByPath(path2);
        var repository3 = service.GetRepositoryByPath(path3);

        // Assert - All should be the same instance
        Assert.Same(repository1, repository2);
        Assert.Same(repository1, repository3);
    }

    [Fact]
    public void InvalidateCache_ShouldClearAllRepositories()
    {
        // Arrange
        var repoPath = CreateBareRepository();
        var service = new GitRepositoryService();
        var repository1 = service.GetRepositoryByPath(repoPath);

        // Act
        service.InvalidateCache();
        var repository2 = service.GetRepositoryByPath(repoPath);

        // Assert - Should be different instances after cache invalidation
        Assert.NotSame(repository1, repository2);
    }

    [Fact]
    public void InvalidateRepository_WithValidPath_ShouldRemoveFromCache()
    {
        // Arrange
        var repoPath = CreateBareRepository();
        var service = new GitRepositoryService();
        var repository1 = service.GetRepositoryByPath(repoPath);

        // Act
        service.InvalidateRepository(repoPath);
        var repository2 = service.GetRepositoryByPath(repoPath);

        // Assert - Should be different instances after invalidation
        Assert.NotSame(repository1, repository2);
    }

    [Fact]
    public void InvalidateRepository_WithNonCachedPath_ShouldNotThrow()
    {
        // Arrange
        var service = new GitRepositoryService();
        var nonCachedPath = Path.Combine(_testRoot, "non-cached");

        // Act & Assert - Should not throw
        service.InvalidateRepository(nonCachedPath);
    }

    [Fact]
    public void InvalidateRepository_WithNullPath_ShouldNotThrow()
    {
        // Arrange
        var service = new GitRepositoryService();

        // Act & Assert - Should not throw
        service.InvalidateRepository(null!);
    }

    [Fact]
    public void InvalidateRepository_WithEmptyPath_ShouldNotThrow()
    {
        // Arrange
        var service = new GitRepositoryService();

        // Act & Assert - Should not throw
        service.InvalidateRepository("");
    }

    [Fact]
    public void InvalidateRepository_ShouldOnlyInvalidateSpecificRepository()
    {
        // Arrange
        var repoPath1 = CreateBareRepository("repo1");
        var repoPath2 = CreateBareRepository("repo2");
        var service = new GitRepositoryService();
        
        var repository1a = service.GetRepositoryByPath(repoPath1);
        var repository2a = service.GetRepositoryByPath(repoPath2);

        // Act
        service.InvalidateRepository(repoPath1);
        
        var repository1b = service.GetRepositoryByPath(repoPath1);
        var repository2b = service.GetRepositoryByPath(repoPath2);

        // Assert
        Assert.NotSame(repository1a, repository1b); // repo1 invalidated
        Assert.Same(repository2a, repository2b);     // repo2 still cached
    }

    [Fact]
    public void GetRepositoryByPath_IsThreadSafe()
    {
        // Arrange
        var repoPath = CreateBareRepository();
        var service = new GitRepositoryService();
        var repositories = new System.Collections.Concurrent.ConcurrentBag<IGitRepository>();

        // Act - Access from multiple threads
        Parallel.For(0, 10, _ =>
        {
            var repo = service.GetRepositoryByPath(repoPath);
            repositories.Add(repo);
        });

        // Assert - All should be the same instance
        var firstRepo = repositories.First();
        Assert.All(repositories, repo => Assert.Same(firstRepo, repo));
    }

    [Fact]
    public void GetRepositoryByPath_WithRelativePath_ShouldNormalizeToAbsolute()
    {
        // Arrange
        var repoPath = CreateBareRepository();
        var service = new GitRepositoryService();
        
        var currentDir = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = _testRoot;
            var relativePath = Path.GetRelativePath(_testRoot, repoPath);
            
            // Act
            var repository1 = service.GetRepositoryByPath(repoPath);
            var repository2 = service.GetRepositoryByPath(relativePath);

            // Assert - Should be same instance despite different path formats
            Assert.Same(repository1, repository2);
        }
        finally
        {
            Environment.CurrentDirectory = currentDir;
        }
    }

    private string CreateBareRepository(string name = "test-repo")
    {
        var path = Path.Combine(_testRoot, name + ".git");
        Directory.CreateDirectory(path);
        
        // Create minimal bare repository structure
        Directory.CreateDirectory(Path.Combine(path, "objects"));
        Directory.CreateDirectory(Path.Combine(path, "refs", "heads"));
        Directory.CreateDirectory(Path.Combine(path, "refs", "tags"));
        
        File.WriteAllText(Path.Combine(path, "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(path, "config"), "[core]\n\trepositoryformatversion = 0\n\tfilemode = false\n\tbare = true\n");
        
        return path;
    }

    public void Dispose()
    {
        TestHelper.TryDeleteDirectory(_testRoot);
    }
}
