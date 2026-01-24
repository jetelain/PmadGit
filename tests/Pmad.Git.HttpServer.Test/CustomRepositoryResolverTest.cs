using Microsoft.AspNetCore.Http;
using Pmad.Git.HttpServer;
using Pmad.Git.LocalRepositories;
using System.Diagnostics;

namespace Pmad.Git.HttpServer.Test;

public sealed class CustomRepositoryResolverTest : IDisposable
{
    private readonly string _serverRepoRoot;
    private readonly string _testRepoPath;

    public CustomRepositoryResolverTest()
    {
        _serverRepoRoot = Path.Combine(Path.GetTempPath(), "PmadGitCustomResolverTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_serverRepoRoot);
        _testRepoPath = Path.Combine(_serverRepoRoot, "test-repo.git");
        CreateBareRepository(_testRepoPath);
    }

    [Fact]
    public async Task WithMultipleParameters_ShouldCombineIntoPath()
    {
        // Arrange
        var orgRepoPath = Path.Combine(_serverRepoRoot, "myorg", "myrepo.git");
        CreateBareRepository(orgRepoPath);

        var options = new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            RepositoryResolver = context =>
            {
                var org = context.Request.RouteValues["organization"]?.ToString();
                var repo = context.Request.RouteValues["repository"]?.ToString();
                return string.IsNullOrEmpty(org) || string.IsNullOrEmpty(repo)
                    ? null
                    : $"{org}/{repo}";
            }
        };
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);

        var context = CreateHttpContext("/git/myorg/myrepo.git/info/refs?service=git-upload-pack");
        context.Request.RouteValues["organization"] = "myorg";
        context.Request.RouteValues["repository"] = "myrepo";

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task WithSingleRepository_ShouldUseFixedName()
    {
        // Arrange
        var options = new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            RepositoryResolver = context => "test-repo"
        };
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);

        var context = CreateHttpContext("/git/info/refs?service=git-upload-pack");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task WithQueryStringResolver_ShouldExtractFromQuery()
    {
        // Arrange
        var options = new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            RepositoryResolver = context =>
            {
                return context.Request.Query["repo"].FirstOrDefault();
            }
        };
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);

        var context = CreateHttpContext("/git/info/refs?repo=test-repo&service=git-upload-pack");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task WithHeaderResolver_ShouldExtractFromHeader()
    {
        // Arrange
        var options = new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            RepositoryResolver = context =>
            {
                return context.Request.Headers["X-Git-Repository"].FirstOrDefault();
            }
        };
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);

        var context = CreateHttpContext("/git/info/refs?service=git-upload-pack");
        context.Request.Headers["X-Git-Repository"] = "test-repo";

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task WithCustomResolverReturningNull_ShouldReturn404()
    {
        // Arrange
        var options = new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            RepositoryResolver = context => null
        };
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);

        var context = CreateHttpContext("/git/info/refs?service=git-upload-pack");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task WithCustomResolverReturningEmpty_ShouldReturn404()
    {
        // Arrange
        var options = new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            RepositoryResolver = context => ""
        };
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);

        var context = CreateHttpContext("/git/info/refs?service=git-upload-pack");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task WithNoCustomResolver_ShouldUseDefaultBehavior()
    {
        // Arrange - Not setting RepositoryResolver, should use default
        var options = new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot
        };
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);

        var context = CreateHttpContext("/git/test-repo.git/info/refs?service=git-upload-pack");
        context.Request.RouteValues["repository"] = "test-repo";

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task WithCatchAllParameter_ShouldCaptureEntirePath()
    {
        // Arrange
        var nestedRepoPath = Path.Combine(_serverRepoRoot, "org", "team", "project.git");
        CreateBareRepository(nestedRepoPath);

        var options = new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            RepositoryResolver = context =>
            {
                // Catch-all parameter captures the entire path
                return context.Request.RouteValues["**path"]?.ToString();
            }
        };
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);

        var context = CreateHttpContext("/git/org/team/project.git/info/refs?service=git-upload-pack");
        context.Request.RouteValues["**path"] = "org/team/project";

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    private HttpContext CreateHttpContext(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.Method = "GET";
        
        if (path.Contains('?'))
        {
            var queryStart = path.IndexOf('?');
            context.Request.QueryString = new QueryString(path.Substring(queryStart));
        }
        
        context.Response.Body = new MemoryStream();
        return context;
    }

    private void CreateBareRepository(string path)
    {
        Directory.CreateDirectory(path);
        RunGitInDirectory(path, "init --bare --quiet --initial-branch=main");
    }

    private void RunGitInDirectory(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start git process");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {arguments} failed with exit code {process.ExitCode}:{Environment.NewLine}{error}");
        }
    }

    public void Dispose()
    {
        TestHelper.TryDeleteDirectory(_serverRepoRoot);
    }
}
