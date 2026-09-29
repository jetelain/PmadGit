namespace Pmad.Git.CliEmulator.Approval;

/// <summary>
/// Context passed to <see cref="IUserApproval.ApproveWriteRemoteAsync"/> describing
/// a push operation.
/// </summary>
public sealed class WriteRemoteContext
{
    /// <summary>Name of the remote (e.g. "origin").</summary>
    public string RemoteName { get; init; } = string.Empty;

    /// <summary>Remote URL (credentials stripped if present).</summary>
    public string RemoteUrl { get; init; } = string.Empty;

    /// <summary>Local branch being pushed.</summary>
    public string BranchName { get; init; } = string.Empty;

    /// <summary><see langword="true"/> if a force-push was requested.</summary>
    public bool IsForce { get; init; }

    /// <summary>Number of local commits ahead of the remote tracking branch.</summary>
    public int CommitsAhead { get; init; }
}
