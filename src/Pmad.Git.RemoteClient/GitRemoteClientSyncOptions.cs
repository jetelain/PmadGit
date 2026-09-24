using Pmad.Git.LocalRepositories;

namespace Pmad.Git.RemoteClient;

/// <summary>
/// Options controlling how a <see cref="GitRepositorySynchronizer"/> synchronizes a local
/// repository with its remote counterpart using the managed <see cref="GitRemoteClientRepository"/>.
/// </summary>
public class GitRemoteClientSyncOptions : GitSyncOptions
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitRemoteClientSyncOptions"/> class with default settings.
    /// </summary>
    public GitRemoteClientSyncOptions()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitRemoteClientSyncOptions"/> class with a remote URL and optional credentials.
    /// </summary>
    /// <param name="url">The URL of the remote Git repository.</param>
    /// <param name="credentials">Optional HTTP credentials used for remote communication.</param>
    public GitRemoteClientSyncOptions(string? url, GitHttpCredentials? credentials = null)
    {
        Url = url;
        Credentials = credentials;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitRemoteClientSyncOptions"/> class copying settings from <paramref name="options"/>.
    /// </summary>
    /// <param name="options">The options to copy from.</param>
    public GitRemoteClientSyncOptions(GitSyncOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Remote = options.Remote;
        Branch = options.Branch;
        PullInterval = options.PullInterval;
        PushDebounceDelay = options.PushDebounceDelay;
        if (options is GitRemoteClientSyncOptions remoteOptions)
        {
            Url = remoteOptions.Url;
            Credentials = remoteOptions.Credentials;
            ClientOptions = remoteOptions.ClientOptions;
        }
    }

    /// <summary>
    /// Gets or sets the URL of the remote Git repository.
    /// When <see langword="null"/>, the repository's configured default remote URL or config is used.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Alias for <see cref="Url"/>.
    /// </summary>
    public string? RemoteUrl
    {
        get => Url;
        set => Url = value;
    }

    /// <summary>
    /// Gets or sets the HTTP authentication credentials used for requests to the remote repository.
    /// </summary>
    public GitHttpCredentials? Credentials { get; set; }

    /// <summary>
    /// Gets or sets additional remote client options, such as custom <see cref="HttpClient"/> or timeout.
    /// </summary>
    public GitRemoteClientOptions? ClientOptions { get; set; }
}

