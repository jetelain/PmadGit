using Microsoft.AspNetCore.Http;

namespace Pmad.Git.HttpServer;

/// <summary>
/// Configuration options for the Git Smart HTTP protocol service.
/// </summary>
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
    /// Parameters are the current HTTP context, the normalized repository name, the operation type (Read or Write), and a cancellation token.
    /// By default, only read operations (fetch/clone) are allowed. Write operations (push) are denied for security.
    /// </summary>
    public Func<HttpContext, string, GitOperation, CancellationToken, ValueTask<bool>>? AuthorizeAsync { get; set; }
        = static (_, _, operation, _) => ValueTask.FromResult(operation == GitOperation.Read);

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

    /// <summary>
    /// Optional callback invoked after a receive-pack (push) operation completes, even for partial success.
    /// Parameters are the HTTP context, the normalized repository name, and a list of successfully updated references.
    /// This callback is invoked whenever at least one reference update succeeds, regardless of whether other
    /// reference updates in the same push operation failed. The list contains only the references that were
    /// successfully updated. This allows the host application to perform cache invalidation or other post-push
    /// actions for the successful updates.
    /// 
    /// The callback is executed asynchronously in the background after the Git protocol response has been sent
    /// to the client. This means that exceptions thrown by the callback will not cause the push to appear to
    /// fail to the client, and slow callbacks will not block the Git client. The callback should handle its
    /// own logging and error handling as needed.
    /// </summary>
    public Func<HttpContext, string, IReadOnlyList<string>, ValueTask>? OnReceivePackCompleted { get; set; }
}
