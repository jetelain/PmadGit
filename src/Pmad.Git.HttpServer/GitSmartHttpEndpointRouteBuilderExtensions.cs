using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace Pmad.Git.HttpServer;

/// <summary>
/// Extension methods for <see cref="IEndpointRouteBuilder"/> to add Git Smart HTTP protocol endpoints.
/// </summary>
public static class GitSmartHttpEndpointRouteBuilderExtensions
{
    private const string InfoRefsSuffix = "/info/refs";
    private const string UploadPackSuffix = "/git-upload-pack";
    private const string ReceivePackSuffix = "/git-receive-pack";

    /// <summary>
    /// Maps Git Smart HTTP endpoints using a service instance from dependency injection.
    /// Call <see cref="GitSmartHttpServiceCollectionExtensions.AddGitSmartHttp(IServiceCollection, GitSmartHttpOptions)"/> 
    /// to register the service first.
    /// </summary>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add routes to.</param>
    /// <param name="pattern">The route pattern. Can contain any number of parameters or none.</param>
    /// <returns>The <see cref="IEndpointRouteBuilder"/> so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when GitSmartHttpService is not registered in DI.</exception>
    public static IEndpointRouteBuilder MapGitSmartHttp(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern = "/git/{*repository}.git")
    {
        if (endpoints is null)
        {
            throw new ArgumentNullException(nameof(endpoints));
        }

        var service = endpoints.ServiceProvider.GetService<GitSmartHttpService>();
        if (service is null)
        {
            throw new InvalidOperationException(
                "GitSmartHttpService is not registered. " +
                "Call services.AddGitSmartHttp() in your service configuration.");
        }

        var catchAllIndex = pattern.IndexOf("{*", StringComparison.Ordinal);
        if (catchAllIndex >= 0)
        {
            var closingBrace = pattern.IndexOf('}', catchAllIndex);
            var paramToken = closingBrace > catchAllIndex ? pattern.Substring(catchAllIndex + 1, closingBrace - catchAllIndex - 1) : "repository";
            var prefix = pattern[..catchAllIndex].TrimEnd('/');
            var catchAllRoute = string.IsNullOrEmpty(prefix) ? "{*gitSmartHttpPath}" : $"{prefix}/{{*gitSmartHttpPath}}";

            endpoints.MapGet(catchAllRoute, (HttpContext context, CancellationToken cancellationToken) =>
            {
                var rawPath = context.Request.RouteValues["gitSmartHttpPath"]?.ToString();
                if (rawPath != null && rawPath.EndsWith(InfoRefsSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    var repo = rawPath[..^InfoRefsSuffix.Length];
                    SetRepoRouteValues(context, paramToken, repo);
                    return service.HandleInfoRefsAsync(context, cancellationToken);
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            });

            endpoints.MapPost(catchAllRoute, (HttpContext context, CancellationToken cancellationToken) =>
            {
                var rawPath = context.Request.RouteValues["gitSmartHttpPath"]?.ToString();
                if (rawPath != null)
                {
                    if (rawPath.EndsWith(UploadPackSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        var repo = rawPath[..^UploadPackSuffix.Length];
                        SetRepoRouteValues(context, paramToken, repo);
                        return service.HandleUploadPackAsync(context, cancellationToken);
                    }

                    if (rawPath.EndsWith(ReceivePackSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        var repo = rawPath[..^ReceivePackSuffix.Length];
                        SetRepoRouteValues(context, paramToken, repo);
                        return service.HandleReceivePackAsync(context, cancellationToken);
                    }
                }

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            });

            return endpoints;
        }

        var patterns = new List<string>();
        if (pattern.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            patterns.Add(pattern);
            patterns.Add(pattern[..^4]);
        }
        else
        {
            patterns.Add(pattern);
            patterns.Add(pattern + ".git");
        }

        foreach (var p in patterns)
        {
            var parsedPattern = RoutePatternFactory.Parse(p);
            var group = endpoints.MapGroup(parsedPattern);

            group.MapGet("/info/refs", (HttpContext context, CancellationToken cancellationToken) =>
                service.HandleInfoRefsAsync(context, cancellationToken));

            group.MapPost("/git-upload-pack", (HttpContext context, CancellationToken cancellationToken) =>
                service.HandleUploadPackAsync(context, cancellationToken));

            group.MapPost("/git-receive-pack", (HttpContext context, CancellationToken cancellationToken) =>
                service.HandleReceivePackAsync(context, cancellationToken));
        }

        return endpoints;
    }

    private static void SetRepoRouteValues(HttpContext context, string paramToken, string repo)
    {
        context.Request.RouteValues["repository"] = repo;
        context.Request.RouteValues[paramToken] = repo;
        var cleanToken = paramToken.TrimStart('*');
        if (cleanToken.Length > 0)
        {
            context.Request.RouteValues[cleanToken] = repo;
        }
    }
}

