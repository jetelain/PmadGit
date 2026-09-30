namespace Pmad.Git.CliEmulator.Approval;

/// <summary>
/// Context passed to <see cref="IUserApproval.ApproveReadRemoteAsync"/> describing
/// an operation that reads from a remote (fetch, pull).
/// </summary>
public sealed class ReadRemoteContext : IApprovalContext
{
    /// <summary>Name of the remote (e.g. "origin").</summary>
    public string RemoteName { get; init; } = string.Empty;

    /// <summary>Remote URL (credentials stripped if present).</summary>
    public string RemoteUrl { get; init; } = string.Empty;

    /// <summary>Branch being fetched, or <see langword="null"/> for all branches.</summary>
    public string? Branch { get; init; }

    /// <summary>Human-readable name of the operation (e.g. "fetch", "pull").</summary>
    public string Operation { get; init; } = string.Empty;
}
