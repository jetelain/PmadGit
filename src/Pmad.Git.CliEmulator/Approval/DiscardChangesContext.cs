namespace Pmad.Git.CliEmulator.Approval;

/// <summary>
/// Context passed to <see cref="IUserApproval.ApproveDiscardLocalChangesAsync"/> describing
/// working-tree changes that will be permanently lost.
/// </summary>
public sealed class DiscardChangesContext : IApprovalContext
{
    /// <summary>Human-readable name of the operation (e.g. "reset --hard", "restore").</summary>
    public string Operation { get; init; } = string.Empty;

    /// <summary>
    /// Repository-relative paths of files whose working-tree content will be overwritten or deleted.
    /// May be empty if the exact file list could not be determined ahead of time.
    /// </summary>
    public IReadOnlyList<string> AffectedFiles { get; init; } = [];

    /// <summary>Optional additional description for the UI.</summary>
    public string? Details { get; init; }
}
