using Microsoft.Extensions.DependencyInjection;
using Pmad.Git.HttpServer;

namespace Pmad.Git.HttpServer.Test;

public sealed class GitSmartHttpServiceCollectionExtensionsTest
{
    [Fact]
    public void AddGitSmartHttp_WithOptions_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new GitSmartHttpOptions
        {
            RepositoryRoot = Path.GetTempPath()
        };

        // Act
        services.AddGitSmartHttp(options);
        var provider = services.BuildServiceProvider();

        // Assert
        var registeredOptions = provider.GetService<GitSmartHttpOptions>();
        Assert.NotNull(registeredOptions);
        Assert.Same(options, registeredOptions);

        var service = provider.GetService<GitSmartHttpService>();
        Assert.NotNull(service);
    }

    [Fact]
    public void AddGitSmartHttp_WithConfigureAction_ShouldRegisterServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var repositoryRoot = Path.GetTempPath();

        // Act
        services.AddGitSmartHttp(options =>
        {
            options.RepositoryRoot = repositoryRoot;
            options.EnableUploadPack = true;
            options.EnableReceivePack = false;
        });
        var provider = services.BuildServiceProvider();

        // Assert
        var registeredOptions = provider.GetService<GitSmartHttpOptions>();
        Assert.NotNull(registeredOptions);
        Assert.Equal(repositoryRoot, registeredOptions.RepositoryRoot);
        Assert.True(registeredOptions.EnableUploadPack);
        Assert.False(registeredOptions.EnableReceivePack);

        var service = provider.GetService<GitSmartHttpService>();
        Assert.NotNull(service);
    }

    [Fact]
    public void AddGitSmartHttp_RegistersAsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new GitSmartHttpOptions
        {
            RepositoryRoot = Path.GetTempPath()
        };

        // Act
        services.AddGitSmartHttp(options);
        var provider = services.BuildServiceProvider();

        // Assert
        var service1 = provider.GetService<GitSmartHttpService>();
        var service2 = provider.GetService<GitSmartHttpService>();
        Assert.NotNull(service1);
        Assert.NotNull(service2);
        Assert.Same(service1, service2); // Should be the same instance
    }

    [Fact]
    public void AddGitSmartHttp_CalledMultipleTimes_DoesNotDuplicate()
    {
        // Arrange
        var services = new ServiceCollection();
        var options1 = new GitSmartHttpOptions { RepositoryRoot = Path.GetTempPath() };
        var options2 = new GitSmartHttpOptions { RepositoryRoot = Path.GetTempPath() };

        // Act
        services.AddGitSmartHttp(options1);
        services.AddGitSmartHttp(options2);
        var provider = services.BuildServiceProvider();

        // Assert - Should use the first registered options (TryAdd behavior)
        var registeredOptions = provider.GetService<GitSmartHttpOptions>();
        Assert.NotNull(registeredOptions);
        Assert.Same(options1, registeredOptions);
    }

    [Fact]
    public void AddGitSmartHttp_WithNullServices_ShouldThrowArgumentNullException()
    {
        // Arrange
        IServiceCollection services = null!;
        var options = new GitSmartHttpOptions { RepositoryRoot = Path.GetTempPath() };

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => services.AddGitSmartHttp(options));
    }

    [Fact]
    public void AddGitSmartHttp_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        GitSmartHttpOptions options = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => services.AddGitSmartHttp(options));
    }

    [Fact]
    public void AddGitSmartHttp_WithNullConfigureAction_ShouldThrowArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        Action<GitSmartHttpOptions> configureOptions = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => services.AddGitSmartHttp(configureOptions));
    }

    [Fact]
    public void AddGitSmartHttp_ReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();
        var options = new GitSmartHttpOptions { RepositoryRoot = Path.GetTempPath() };

        // Act
        var result = services.AddGitSmartHttp(options);

        // Assert
        Assert.Same(services, result);
    }

    [Fact]
    public void AddGitSmartHttp_WithConfigureAction_ReturnsServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddGitSmartHttp(options =>
        {
            options.RepositoryRoot = Path.GetTempPath();
        });

        // Assert
        Assert.Same(services, result);
    }

    [Fact]
    public void AddGitSmartHttp_WithEmptyRepositoryRoot_ServiceConstructionShouldFail()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddGitSmartHttp(options =>
        {
            options.RepositoryRoot = ""; // Invalid
        });
        var provider = services.BuildServiceProvider();

        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => provider.GetRequiredService<GitSmartHttpService>());
        Assert.Contains("Repository root must be provided", exception.Message);
    }

    [Fact]
    public void AddGitSmartHttp_AllowsChainingWithOtherServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services
            .AddGitSmartHttp(options => options.RepositoryRoot = Path.GetTempPath())
            .AddSingleton<IDummyService, DummyService>();

        var provider = services.BuildServiceProvider();

        // Assert
        Assert.NotNull(provider.GetService<GitSmartHttpService>());
        Assert.NotNull(provider.GetService<IDummyService>());
    }

    // Helper interface and class for chaining test
    private interface IDummyService { }
    private class DummyService : IDummyService { }
}
