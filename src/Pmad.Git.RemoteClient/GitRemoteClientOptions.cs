namespace Pmad.Git.RemoteClient;

/// <summary>
/// Options for configuring a managed Git Smart HTTP client.
/// </summary>
public sealed class GitRemoteClientOptions
{
    /// <summary>
    /// Gets or sets the HTTP authentication credentials used for requests to the remote repository.
    /// </summary>
    public GitHttpCredentials? Credentials { get; set; }

    /// <summary>
    /// Gets or sets an optional <see cref="HttpClient"/> instance to use for HTTP communication.
    /// When <see langword="null"/>, a shared default client is used.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>
    /// Gets or sets the user agent string advertised during Git capability negotiation.
    /// Defaults to <c>pmad-git/1.0</c>.
    /// </summary>
    public string? Agent { get; set; } = "pmad-git/1.0";

    /// <summary>
    /// Gets or sets the HTTP request timeout. Defaults to 5 minutes.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets an optional callback invoked with server progress messages (sideband channel 2).
    /// </summary>
    public Action<string>? OnProgress { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether credentials embedded in remote URLs
    /// should be stripped before persisting to .git/config during clone.
    /// Defaults to <see langword="false"/> to match standard Git CLI behavior.
    /// </summary>
    public bool SanitizeRemoteUrlInConfig { get; set; }
}

