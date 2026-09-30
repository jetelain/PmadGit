using Pmad.Git.CliEmulator;
using Pmad.Git.CliEmulator.Approval;

namespace Pmad.Git.CliEmulator.AI.Test;

/// <summary>
/// An <see cref="IUserApproval"/> that denies every operation.
/// Used in tests that verify denial / exit-130 behaviour.
/// </summary>
internal sealed class AlwaysDenyApproval : IUserApproval
{
    public CancellationToken CancellationToken => CancellationToken.None;

    public Task<bool> ApproveDiscardLocalChangesAsync(DiscardChangesContext context) =>
        Task.FromResult(false);

    public Task<bool> ApproveHistoryRewriteAsync(HistoryRewriteContext context) =>
        Task.FromResult(false);

    public Task<bool> ApproveReadRemoteAsync(ReadRemoteContext context) =>
        Task.FromResult(false);

    public Task<bool> ApproveUnpushedCommitLossAsync(UnpushedCommitLossContext context) =>
        Task.FromResult(false);

    public Task<bool> ApproveWriteRemoteAsync(WriteRemoteContext context) =>
        Task.FromResult(false);
}
