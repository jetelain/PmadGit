using Pmad.Git.Cli;

namespace Pmad.Git.HttpServer.Test;

public sealed class GitRepositorySynchronizerServiceTest : IDisposable
{
    private readonly string _testRoot;
    private readonly List<GitTestRepository> _repositories = new();

    public GitRepositorySynchronizerServiceTest()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "PmadGitSynchronizerServiceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    private GitTestRepository CreateRepository()
    {
        var repository = GitTestRepository.Create();
        _repositories.Add(repository);
        return repository;
    }

    [Fact]
    public void GetSynchronizerByPath_WithoutSetup_ShouldReturnNull()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act
        var synchronizer = service.GetSynchronizerByPath(repository.WorkingDirectory);

        // Assert
        Assert.Null(synchronizer);
    }

    [Fact]
    public async Task SetupSynchronizer_ShouldCreateAndCacheSynchronizer()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act
        var synchronizer = service.SetupSynchronizer(repository.WorkingDirectory, new GitSyncOptions());

        // Assert
        Assert.NotNull(synchronizer);
        Assert.Same(synchronizer, service.GetSynchronizerByPath(repository.WorkingDirectory));

        await synchronizer.DisposeAsync();
    }

    [Fact]
    public async Task SetupSynchronizer_CalledTwice_ShouldReplacePreviousInstanceAndDisposeIt()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act
        var synchronizer1 = service.SetupSynchronizer(repository.WorkingDirectory, new GitSyncOptions());
        var synchronizer2 = service.SetupSynchronizer(repository.WorkingDirectory, new GitSyncOptions());

        // Assert
        Assert.NotSame(synchronizer1, synchronizer2);
        Assert.Same(synchronizer2, service.GetSynchronizerByPath(repository.WorkingDirectory));

        await synchronizer2.DisposeAsync();
    }

    [Fact]
    public void SetupSynchronizer_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => service.SetupSynchronizer(repository.WorkingDirectory, null!));
    }

    [Fact]
    public void SetupSynchronizer_WithNonExistentPath_ShouldThrowDirectoryNotFoundException()
    {
        // Arrange
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        var nonExistentPath = Path.Combine(_testRoot, "non-existent");

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => service.SetupSynchronizer(nonExistentPath, new GitSyncOptions()));
    }

    [Fact]
    public void GetSynchronizerByPath_WithNonExistentPath_ShouldThrowDirectoryNotFoundException()
    {
        // Arrange
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        var nonExistentPath = Path.Combine(_testRoot, "non-existent");

        // Act & Assert
        Assert.Throws<DirectoryNotFoundException>(() => service.GetSynchronizerByPath(nonExistentPath));
    }

    [Fact]
    public async Task InvalidateSynchronizer_ShouldRemoveAndDisposeCachedSynchronizer()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);
        service.SetupSynchronizer(repository.WorkingDirectory, new GitSyncOptions());

        // Act
        service.InvalidateSynchronizer(repository.WorkingDirectory);

        // Assert
        Assert.Null(service.GetSynchronizerByPath(repository.WorkingDirectory));

        await Task.Delay(10); // allow fire-and-forget DisposeAsync to complete
    }

    [Fact]
    public void InvalidateSynchronizer_WithNoCachedSynchronizer_ShouldNotThrow()
    {
        // Arrange
        var repository = CreateRepository();
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act & Assert (should not throw)
        service.InvalidateSynchronizer(repository.WorkingDirectory);
    }

    [Fact]
    public void InvalidateSynchronizer_WithNullOrWhitespacePath_ShouldNotThrow()
    {
        // Arrange
        var repositoryService = new GitRepositoryService();
        var service = new GitRepositorySynchronizerService(repositoryService);

        // Act & Assert (should not throw)
        service.InvalidateSynchronizer(null!);
        service.InvalidateSynchronizer("");
        service.InvalidateSynchronizer("   ");
    }

    [Fact]
    public void Constructor_WithNullRepositoryService_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new GitRepositorySynchronizerService(null!));
    }

    public void Dispose()
    {
        foreach (var repository in _repositories)
        {
            repository.Dispose();
        }
        TestHelper.TryDeleteDirectory(_testRoot);
    }
}
