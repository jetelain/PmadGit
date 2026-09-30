using Pmad.Git.CliEmulator.Approval;

namespace Pmad.Git.CliEmulator;

/// <summary>
/// An <see cref="IUserApproval"/> implementation that automatically approves every operation.
/// Used as the default when no explicit approval gate is provided to the MEAI integration.
/// </summary>
public sealed class AutoApproval : IUserApproval
{
    /// <summary>
    /// Initializes a new <see cref="AutoApproval"/> with the given cancellation token.
    /// </summary>
    public AutoApproval(CancellationToken cancellationToken = default)
    {
        CancellationToken = cancellationToken;
    }

    /// <inheritdoc/>
    public CancellationToken CancellationToken { get; }

    /// <inheritdoc/>
    public Task<bool> ApproveDiscardLocalChangesAsync(DiscardChangesContext context) =>
        Task.FromResult(true);

    /// <inheritdoc/>
    public Task<bool> ApproveHistoryRewriteAsync(HistoryRewriteContext context) =>
        Task.FromResult(true);

    /// <inheritdoc/>
    public Task<bool> ApproveReadRemoteAsync(ReadRemoteContext context) =>
        Task.FromResult(true);

    /// <inheritdoc/>
    public Task<bool> ApproveUnpushedCommitLossAsync(UnpushedCommitLossContext context) =>
        Task.FromResult(true);

    /// <inheritdoc/>
    public Task<bool> ApproveWriteRemoteAsync(WriteRemoteContext context) =>
        Task.FromResult(true);
}