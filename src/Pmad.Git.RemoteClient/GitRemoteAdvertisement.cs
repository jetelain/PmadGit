using Pmad.Git.LocalRepositories;

namespace Pmad.Git.RemoteClient;

/// <summary>
/// Represents references and capabilities advertised by a Git remote repository during reference discovery.
/// </summary>
public sealed class GitRemoteAdvertisement
{
    /// <summary>
    /// Gets the map of reference names (e.g. <c>refs/heads/main</c>) to commit hashes.
    /// </summary>
    public IReadOnlyDictionary<string, GitHash> References { get; }

    /// <summary>
    /// Gets the capabilities advertised by the remote server.
    /// </summary>
    public IReadOnlySet<string> Capabilities { get; }

    /// <summary>
    /// Gets the map of symbolic reference names to target reference names (e.g. <c>HEAD</c> -&gt; <c>refs/heads/main</c>).
    /// </summary>
    public IReadOnlyDictionary<string, string> Symrefs { get; }

    /// <summary>
    /// Gets the target of the HEAD symbolic reference, if advertised (e.g. <c>refs/heads/main</c>).
    /// </summary>
    public string? HeadSymrefTarget { get; }

    /// <summary>
    /// Gets the commit hash that HEAD points to, if any.
    /// </summary>
    public GitHash? HeadHash { get; }

    /// <summary>
    /// Gets the advertised object format.
    /// </summary>
    public GitObjectFormat ObjectFormat { get; }

    /// <summary>
    /// Gets the agent string advertised by the remote server, if any.
    /// </summary>
    public string? Agent { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitRemoteAdvertisement"/> class.
    /// </summary>
    public GitRemoteAdvertisement(
        IReadOnlyDictionary<string, GitHash> references,
        IReadOnlySet<string> capabilities,
        IReadOnlyDictionary<string, string> symrefs,
        string? headSymrefTarget,
        GitHash? headHash,
        GitObjectFormat objectFormat = GitObjectFormat.Sha1,
        string? agent = null)
    {
        References = references ?? throw new ArgumentNullException(nameof(references));
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        Symrefs = symrefs ?? throw new ArgumentNullException(nameof(symrefs));
        HeadSymrefTarget = headSymrefTarget;
        HeadHash = headHash;
        ObjectFormat = objectFormat;
        Agent = agent;
    }
}

