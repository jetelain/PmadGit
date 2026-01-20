using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Pmad.Git.HttpServer;

public sealed class GitSmartHttpOptions
{
    /// <summary>
    /// Gets or sets the absolute path containing all repositories served by the HTTP endpoints.
    /// </summary>
    public string RepositoryRoot { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional route prefix (for example "git") used by the endpoint mapping helper.
    /// </summary>
    public string RoutePrefix { get; set; } = "git";

    /// <summary>
    /// Allows disabling fetch/clone operations.
    /// </summary>
    public bool EnableUploadPack { get; set; } = true;

    /// <summary>
    /// Allows disabling push operations.
    /// </summary>
    public bool EnableReceivePack { get; set; } = true;

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
}
