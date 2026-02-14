using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Pmad.Git.HttpServer;
using Pmad.Git.LocalRepositories;
using System.Diagnostics;

namespace Pmad.Git.HttpServer.Test;

public sealed class RepositoryNameValidationTest : IDisposable
{
    private readonly string _serverRepoRoot;

    public RepositoryNameValidationTest()
    {
        _serverRepoRoot = Path.Combine(Path.GetTempPath(), "PmadGitNameValidationTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_serverRepoRoot);
        CreateBareRepository(Path.Combine(_serverRepoRoot, "valid-repo.git"));
    }

    #region Default Validator Tests

    [Fact]
    public async Task HandleInfoRefsAsync_WithValidAlphanumericName_ShouldSucceed()
    {
        // Arrange
        CreateBareRepository(Path.Combine(_serverRepoRoot, "test123.git"));
        var service = CreateServiceWithDefaultValidator();
        var context = CreateHttpContext("/test123.git/info/refs?service=git-upload-pack", repository: "test123");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithHyphensInName_ShouldSucceed()
    {
        // Arrange
        CreateBareRepository(Path.Combine(_serverRepoRoot, "test-repo.git"));
        var service = CreateServiceWithDefaultValidator();
        var context = CreateHttpContext("/test-repo.git/info/refs?service=git-upload-pack", repository: "test-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithUnderscoresInName_ShouldSucceed()
    {
        // Arrange
        CreateBareRepository(Path.Combine(_serverRepoRoot, "test_repo.git"));
        var service = CreateServiceWithDefaultValidator();
        var context = CreateHttpContext("/test_repo.git/info/refs?service=git-upload-pack", repository: "test_repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithForwardSlashesInName_ShouldSucceed()
    {
        // Arrange
        CreateBareRepository(Path.Combine(_serverRepoRoot, "org", "project.git"));
        var service = CreateServiceWithDefaultValidator();
        var context = CreateHttpContext("/org/project.git/info/refs?service=git-upload-pack", repository: "org/project");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithSpacesInName_ShouldReturn400()
    {
        // Arrange
        var service = CreateServiceWithDefaultValidator();
        var context = CreateHttpContext("/test repo.git/info/refs?service=git-upload-pack", repository: "test repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithSpecialCharactersInName_ShouldReturn400()
    {
        // Arrange
        var service = CreateServiceWithDefaultValidator();
        var context = CreateHttpContext("/test@repo.git/info/refs?service=git-upload-pack", repository: "test@repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("test$repo")]
    [InlineData("test%repo")]
    [InlineData("test&repo")]
    [InlineData("test*repo")]
    [InlineData("test+repo")]
    [InlineData("test=repo")]
    [InlineData("test[repo")]
    [InlineData("test]repo")]
    [InlineData("test{repo")]
    [InlineData("test}repo")]
    [InlineData("test|repo")]
    [InlineData("test:repo")]
    [InlineData("test;repo")]
    [InlineData("test\"repo")]
    [InlineData("test'repo")]
    [InlineData("test<repo")]
    [InlineData("test>repo")]
    [InlineData("test,repo")]
    [InlineData("test?repo")]
    [InlineData("test!repo")]
    [InlineData("test~repo")]
    [InlineData("test`repo")]
    public async Task HandleInfoRefsAsync_WithInvalidCharacters_ShouldReturn400(string repoName)
    {
        // Arrange
        var service = CreateServiceWithDefaultValidator();
        var context = CreateHttpContext($"/{repoName}.git/info/refs?service=git-upload-pack", repository: repoName);

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithDotInName_ShouldReturn400()
    {
        // Arrange
        var service = CreateServiceWithDefaultValidator();
        var context = CreateHttpContext("/test.repo.git/info/refs?service=git-upload-pack", repository: "test.repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    #endregion

    #region Custom Validator Tests

    [Fact]
    public async Task HandleInfoRefsAsync_WithCustomValidator_ShouldUseIt()
    {
        // Arrange - Custom validator that allows dots
        CreateBareRepository(Path.Combine(_serverRepoRoot, "test.repo.git"));
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            RepositoryNameValidator = name =>
            {
                foreach (var c in name)
                {
                    if (!char.IsLetterOrDigit(c) && c != '-' && c != '_' && c != '/' && c != '.')
                    {
                        return false;
                    }
                }
                return true;
            }
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test.repo.git/info/refs?service=git-upload-pack", repository: "test.repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithCustomValidatorReturningFalse_ShouldReturn400()
    {
        // Arrange - Custom validator that rejects all names
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            RepositoryNameValidator = name => false
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/valid-repo.git/info/refs?service=git-upload-pack", repository: "valid-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithNullValidator_ShouldSkipValidation()
    {
        // Arrange - Null validator means no validation
        CreateBareRepository(Path.Combine(_serverRepoRoot, "test@special.git"));
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            RepositoryNameValidator = null
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test@special.git/info/refs?service=git-upload-pack", repository: "test@special");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithValidatorThrowingException_ShouldReturn400()
    {
        // Arrange - Validator that throws a non-InvalidOperationException
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            RepositoryNameValidator = name => throw new ArgumentException("Validator error")
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/valid-repo.git/info/refs?service=git-upload-pack", repository: "valid-repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    #endregion

    #region Validator with Normalizer Tests

    [Fact]
    public async Task HandleInfoRefsAsync_ValidatorAndNormalizer_ShouldApplyBothInOrder()
    {
        // Arrange - Validator runs AFTER normalizer, so it sees the normalized name
        CreateBareRepository(Path.Combine(_serverRepoRoot, "normalized.git"));
        string? validatedName = null;
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            RepositoryNameValidator = name =>
            {
                validatedName = name;
                return name == "normalized"; // Validator sees original name
            },
            RepositoryNameNormalizer = name => "normalized" // Normalizer runs after
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/original.git/info/refs?service=git-upload-pack", repository: "original");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal("normalized", validatedName); // Validator sees normalized, not original
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_ValidatorAfterNormalizer_ShouldValidateUnnormalizedName()
    {
        // Arrange - Validator should see the name after .git removal but after custom normalization
        CreateBareRepository(Path.Combine(_serverRepoRoot, "test-repo.git"));
        string? validatedName = null;
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot,
            RepositoryNameValidator = name =>
            {
                validatedName = name;
                return true;
            },
            RepositoryNameNormalizer = name => name.ToUpperInvariant()
        });
        var repositoryService = new GitRepositoryService();
        var service = new GitSmartHttpService(options, repositoryService);
        var context = CreateHttpContext("/test-repo.git/info/refs?service=git-upload-pack", repository: "test-repo.git");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal("TEST-REPO", validatedName); // .git stripped, and normalized
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task HandleInfoRefsAsync_WithEmptyName_ShouldReturn404()
    {
        // Arrange
        var service = CreateServiceWithDefaultValidator();
        var context = CreateHttpContext("/info/refs?service=git-upload-pack", repository: "");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithOnlySlashes_ShouldReturn400()
    {
        // Arrange
        var service = CreateServiceWithDefaultValidator();
        var context = CreateHttpContext("///.git/info/refs?service=git-upload-pack", repository: "///");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        // After trimming slashes, the name becomes empty which triggers validation failure
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithMixedCase_ShouldSucceed()
    {
        // Arrange
        CreateBareRepository(Path.Combine(_serverRepoRoot, "TestRepo.git"));
        var service = CreateServiceWithDefaultValidator();
        var context = CreateHttpContext("/TestRepo.git/info/refs?service=git-upload-pack", repository: "TestRepo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    #endregion

    #region Security Tests

    [Fact]
    public async Task HandleInfoRefsAsync_WithNullByteInName_ShouldReturn400()
    {
        // Arrange
        var service = CreateServiceWithDefaultValidator();
        var context = CreateHttpContext("/test\0repo.git/info/refs?service=git-upload-pack", repository: "test\0repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithEncodedSpecialCharacters_ShouldReturn400()
    {
        // Arrange
        var service = CreateServiceWithDefaultValidator();
        // Even if URL-encoded, after decoding it should still fail validation
        var context = CreateHttpContext("/test%20repo.git/info/refs?service=git-upload-pack", repository: "test repo");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleInfoRefsAsync_WithPathTraversalStillBlocked_ShouldReturn400()
    {
        // Arrange - Even before validator, path traversal should be blocked
        var service = CreateServiceWithDefaultValidator();
        var context = CreateHttpContext("/../../etc/passwd/info/refs?service=git-upload-pack", repository: "../../etc/passwd");

        // Act
        await service.HandleInfoRefsAsync(context);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    #endregion

    #region Helper Methods

    private GitSmartHttpService CreateServiceWithDefaultValidator()
    {
        var options = Options.Create(new GitSmartHttpOptions
        {
            RepositoryRoot = _serverRepoRoot
            // Use default validator
        });
        var repositoryService = new GitRepositoryService();
        return new GitSmartHttpService(options, repositoryService);
    }

    private HttpContext CreateHttpContext(string path, string? repository = null)
    {
        var context = new DefaultHttpContext();
        var queryIndex = path.IndexOf('?');
        var pathPart = queryIndex >= 0 ? path.Substring(0, queryIndex) : path;
        var queryPart = queryIndex >= 0 ? path.Substring(queryIndex) : string.Empty;
        context.Request.Path = pathPart;
        context.Request.Method = "GET";
        context.Request.QueryString = new QueryString(queryPart);

        if (repository != null)
        {
            context.Request.RouteValues["repository"] = repository;
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

    #endregion
}
