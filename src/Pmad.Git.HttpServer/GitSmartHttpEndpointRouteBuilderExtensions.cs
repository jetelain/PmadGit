using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Pmad.Git.HttpServer;

public static class GitSmartHttpEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps Git Smart HTTP endpoints using a service instance from dependency injection.
    /// Call <see cref="GitSmartHttpServiceCollectionExtensions.AddGitSmartHttp(IServiceCollection, GitSmartHttpOptions)"/> 
    /// to register the service first.
    /// </summary>
    /// <param name="endpoints">The <see cref="IEndpointRouteBuilder"/> to add routes to.</param>
    /// <returns>The <see cref="IEndpointRouteBuilder"/> so that additional calls can be chained.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="endpoints"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when GitSmartHttpService is not registered in DI.</exception>
    public static IEndpointRouteBuilder MapGitSmartHttp(this IEndpointRouteBuilder endpoints)
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

        var options = endpoints.ServiceProvider.GetService<GitSmartHttpOptions>();
        if (options is null)
        {
            throw new InvalidOperationException(
                "GitSmartHttpOptions is not registered. " +
                "Call services.AddGitSmartHttp() in your service configuration.");
        }

        var prefix = NormalizePrefix(options.RoutePrefix);
        var infoRefsPattern = Combine(prefix, "{repository}.git/info/refs");
        var uploadPattern = Combine(prefix, "{repository}.git/git-upload-pack");
        var receivePattern = Combine(prefix, "{repository}.git/git-receive-pack");

        endpoints.MapGet(infoRefsPattern, (HttpContext context, CancellationToken cancellationToken) =>
            service.HandleInfoRefsAsync(context, cancellationToken));

        endpoints.MapPost(uploadPattern, (HttpContext context, CancellationToken cancellationToken) =>
            service.HandleUploadPackAsync(context, cancellationToken));

        endpoints.MapPost(receivePattern, (HttpContext context, CancellationToken cancellationToken) =>
            service.HandleReceivePackAsync(context, cancellationToken));

        return endpoints;
    }

    private static string NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return "/";
        }

        var trimmed = prefix.Trim('/');
        return $"/{trimmed}/";
    }

    private static string Combine(string prefix, string segment)
    {
        if (prefix == "/")
        {
            return "/" + segment;
        }

        return prefix + segment;
    }
}

