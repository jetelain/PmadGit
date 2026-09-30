using Pmad.Git.CliEmulator.Approval;

namespace Pmad.Git.CliEmulator;

/// <summary>
/// An <see cref="IUserApproval"/> implementation that automatically approves every operation.
/// Used when <see langword="null"/> is passed as <c>userApproval</c> to
/// <see cref="IGitCliEmulator.InvokeAsync"/>.
/// </summary>
public sealed class AutoApproval : IUserApproval
{
    /// <summary>
    /// Gets the singleton instance of <see cref="AutoApproval"/>.
    /// </summary>
    public static readonly AutoApproval Instance = new();

    /// <inheritdoc/>
    public Task<ApprovalResult> ApproveDiscardLocalChangesAsync(DiscardChangesContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalResult.Approved);

    /// <inheritdoc/>
    public Task<ApprovalResult> ApproveHistoryRewriteAsync(HistoryRewriteContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalResult.Approved);

    /// <inheritdoc/>
    public Task<ApprovalResult> ApproveReadRemoteAsync(ReadRemoteContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalResult.Approved);

    /// <inheritdoc/>
    public Task<ApprovalResult> ApproveUnpushedCommitLossAsync(UnpushedCommitLossContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalResult.Approved);

    /// <inheritdoc/>
    public Task<ApprovalResult> ApproveWriteRemoteAsync(WriteRemoteContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalResult.Approved);
}
