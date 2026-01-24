using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Pmad.Git.HttpServer;

namespace Pmad.Git.HttpServer.Test;

public sealed class GitSmartHttpEndpointRouteBuilderExtensionsTest
{
    [Fact]
    public void MapGitSmartHttp_WithDI_ShouldNotThrow()
    {
        // Arrange
        var repositoryRoot = CreateTemporaryDirectory();
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddGitSmartHttp(options =>
        {
            options.RepositoryRoot = repositoryRoot;
        });
        var provider = services.BuildServiceProvider();
        
        var endpoints = new TestEndpointRouteBuilder(provider);

        // Act & Assert - should not throw
        var result = endpoints.MapGitSmartHttp();
        
        Assert.NotNull(result);
        Assert.Same(endpoints, result);
        
        CleanupDirectory(repositoryRoot);
    }

    [Fact]
    public void MapGitSmartHttp_WithoutDI_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddRouting();
        // Not adding GitSmartHttp service
        var provider = services.BuildServiceProvider();
        
        var endpoints = new TestEndpointRouteBuilder(provider);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            endpoints.MapGitSmartHttp();
        });
        
        Assert.Contains("GitSmartHttpService is not registered", exception.Message);
        Assert.Contains("AddGitSmartHttp()", exception.Message);
    }

    [Fact]
    public void MapGitSmartHttp_WithNullEndpoints_ShouldThrowArgumentNullException()
    {
        // Arrange
        IEndpointRouteBuilder endpoints = null!;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => endpoints.MapGitSmartHttp());
    }

    [Fact]
    public void MapGitSmartHttp_RegistersEndpoints()
    {
        // Arrange
        var repositoryRoot = CreateTemporaryDirectory();
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddGitSmartHttp(options =>
        {
            options.RepositoryRoot = repositoryRoot;
        });
        var provider = services.BuildServiceProvider();
        
        var endpoints = new TestEndpointRouteBuilder(provider);

        // Act
        endpoints.MapGitSmartHttp();

        // Assert - Should have registered at least one data source
        Assert.NotEmpty(endpoints.DataSources);
        
        CleanupDirectory(repositoryRoot);
    }

    [Fact]
    public void MapGitSmartHttp_WithCustomPrefix_UsesPrefix()
    {
        // Arrange
        var repositoryRoot = CreateTemporaryDirectory();
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddGitSmartHttp(options =>
        {
            options.RepositoryRoot = repositoryRoot;
        });
        var provider = services.BuildServiceProvider();
        
        var endpoints = new TestEndpointRouteBuilder(provider);

        // Act
        endpoints.MapGitSmartHttp("/custom/{repository}.git");

        // Assert - Should have registered at least one data source
        Assert.NotEmpty(endpoints.DataSources);
        
        CleanupDirectory(repositoryRoot);
    }

    [Fact]
    public void MapGitSmartHttp_ReturnsEndpointRouteBuilder()
    {
        // Arrange
        var repositoryRoot = CreateTemporaryDirectory();
        var services = new ServiceCollection();
        services.AddRouting();
        services.AddGitSmartHttp(options =>
        {
            options.RepositoryRoot = repositoryRoot;
        });
        var provider = services.BuildServiceProvider();
        
        var endpoints = new TestEndpointRouteBuilder(provider);

        // Act
        var result = endpoints.MapGitSmartHttp();

        // Assert
        Assert.NotNull(result);
        Assert.Same(endpoints, result);
        
        CleanupDirectory(repositoryRoot);
    }

    #region Helper Methods

    private string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PmadGitDITests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private void CleanupDirectory(string path)
    {
        TestHelper.TryDeleteDirectory(path);
    }

    /// <summary>
    /// Test implementation of IEndpointRouteBuilder for unit testing
    /// </summary>
    private class TestEndpointRouteBuilder : IEndpointRouteBuilder
    {
        public TestEndpointRouteBuilder(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
            DataSources = new List<EndpointDataSource>();
        }

        public IServiceProvider ServiceProvider { get; }

        public ICollection<EndpointDataSource> DataSources { get; }

        public IApplicationBuilder CreateApplicationBuilder()
        {
            throw new NotImplementedException();
        }
    }

    #endregion
}


