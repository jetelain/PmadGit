using Pmad.Git.CliEmulator.Approval;

namespace Pmad.Git.CliEmulator;

/// <summary>
/// An <see cref="IUserApproval"/> implementation that denies every gated operation.
/// </summary>
public sealed class DenyApproval : IUserApproval
{
    /// <summary>
    /// Gets the singleton instance of <see cref="DenyApproval"/>.
    /// </summary>
    public static readonly DenyApproval Instance = new();

    /// <inheritdoc/>
    public Task<ApprovalResult> ApproveDiscardLocalChangesAsync(DiscardChangesContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalResult.Denied);

    /// <inheritdoc/>
    public Task<ApprovalResult> ApproveHistoryRewriteAsync(HistoryRewriteContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalResult.Denied);

    /// <inheritdoc/>
    public Task<ApprovalResult> ApproveReadRemoteAsync(ReadRemoteContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalResult.Denied);

    /// <inheritdoc/>
    public Task<ApprovalResult> ApproveUnpushedCommitLossAsync(UnpushedCommitLossContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalResult.Denied);

    /// <inheritdoc/>
    public Task<ApprovalResult> ApproveWriteRemoteAsync(WriteRemoteContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalResult.Denied);
}
