using Pmad.Git.CliEmulator;
using Pmad.Git.CliEmulator.Approval;

namespace Pmad.Git.CliEmulator.AI.Test;

/// <summary>
/// An <see cref="IUserApproval"/> that denies every operation.
/// Used in tests that verify denial / exit-130 behaviour.
/// </summary>
internal sealed class AlwaysDenyApproval : IUserApproval
{
    public Task<ApprovalResult> ApproveDiscardLocalChangesAsync(DiscardChangesContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalResult.Denied);

    public Task<ApprovalResult> ApproveHistoryRewriteAsync(HistoryRewriteContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalResult.Denied);

    public Task<ApprovalResult> ApproveReadRemoteAsync(ReadRemoteContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalResult.Denied);

    public Task<ApprovalResult> ApproveUnpushedCommitLossAsync(UnpushedCommitLossContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalResult.Denied);

    public Task<ApprovalResult> ApproveWriteRemoteAsync(WriteRemoteContext context, CancellationToken cancellationToken) =>
        Task.FromResult(ApprovalResult.Denied);
}
