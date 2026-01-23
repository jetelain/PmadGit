using Microsoft.AspNetCore.Http;

namespace Pmad.Git.HttpServer;

public sealed class GitSmartHttpOptions
{
    /// <summary>
    /// Gets or sets the absolute path containing all repositories served by the HTTP endpoints.
    /// </summary>
    public string RepositoryRoot { get; set; } = string.Empty;

    /// <summary>
    /// Allows disabling fetch/clone operations.
    /// </summary>
    public bool EnableUploadPack { get; set; } = true;

    /// <summary>
    /// Allows enabling push operations. Disabled by default for security.
    /// </summary>
    public bool EnableReceivePack { get; set; } = false;

    /// <summary>
    /// Gets or sets the agent string advertised to git clients.
    /// </summary>
    public string Agent { get; set; } = "Pmad.Git.HttpServer/1.0";

    /// <summary>
    /// Optional callback that can block access to specific repositories.
    /// Parameters are the current HTTP context and the normalized repository name.
    /// </summary>
    public Func<HttpContext, string, CancellationToken, ValueTask<bool>>? AuthorizeAsync { get; set; }
        = static (_, _, _) => ValueTask.FromResult(true);

    /// <summary>
    /// Optional callback used to sanitize repository names before accessing the file system.
    /// </summary>
    public Func<string, string>? RepositoryNameNormalizer { get; set; }
        = static name => name;

    /// <summary>
    /// Callback used to resolve the repository name from the HTTP context.
    /// By default, extracts the "repository" route parameter.
    /// </summary>
    public Func<HttpContext, string?>? RepositoryResolver { get; set; }
        = static context => context.Request.RouteValues.TryGetValue("repository", out var value) ? value?.ToString() : null;
}
