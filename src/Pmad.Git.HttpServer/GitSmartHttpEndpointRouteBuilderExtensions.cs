using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Pmad.Git.HttpServer;

public static class GitSmartHttpEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapGitSmartHttp(this IEndpointRouteBuilder endpoints, GitSmartHttpOptions options)
    {
        if (endpoints is null)
        {
            throw new ArgumentNullException(nameof(endpoints));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var service = new GitSmartHttpService(options);
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
