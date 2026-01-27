using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
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
    public static IEndpointRouteBuilder MapGitSmartHttp(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern = "/git/{repository}.git")
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

        var parsedPattern = RoutePatternFactory.Parse(pattern);
        var group = endpoints.MapGroup(parsedPattern);

        group.MapGet("/info/refs", (HttpContext context, CancellationToken cancellationToken) =>
            service.HandleInfoRefsAsync(context, cancellationToken));

        group.MapPost("/git-upload-pack", (HttpContext context, CancellationToken cancellationToken) =>
            service.HandleUploadPackAsync(context, cancellationToken));

        group.MapPost("/git-receive-pack", (HttpContext context, CancellationToken cancellationToken) =>
            service.HandleReceivePackAsync(context, cancellationToken));

        return endpoints;
    }
}

