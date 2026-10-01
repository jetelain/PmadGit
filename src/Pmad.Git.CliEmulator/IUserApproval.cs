using Pmad.Git.CliEmulator.Approval;

namespace Pmad.Git.CliEmulator;

/// <summary>
/// Callback interface that the host application implements to gate destructive or network operations.
/// </summary>
/// <remarks>
/// Each method returns an <see cref="ApprovalResult"/>:
/// <list type="bullet">
///   <item><see cref="ApprovalResult.Approved"/> — allow the operation to proceed.</item>
///   <item><see cref="ApprovalResult.Denied"/> — reject the operation; the emulator returns exit code 130 with a denial message.</item>
///   <item><see cref="ApprovalResult.Cancelled"/> — the approval UI was dismissed without a decision; the emulator raises <see cref="OperationCanceledException"/>.</item>
/// </list>
/// The <c>cancellationToken</c> passed to each method is the same token supplied to
/// <see cref="IGitCliEmulator.InvokeAsync"/>. Approval UIs should observe it so they can dismiss
/// themselves if the overall operation is cancelled from outside.
/// </remarks>
public interface IUserApproval
{
    /// <summary>
    /// Called before a network read operation (fetch, pull).
    /// </summary>
    /// <param name="context">Details about the remote and branch being fetched.</param>
    /// <param name="cancellationToken">Token that fires if the overall operation is cancelled.</param>
    Task<ApprovalResult> ApproveReadRemoteAsync(ReadRemoteContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Called before a network write operation (push).
    /// </summary>
    /// <param name="context">Details about the remote, branch, force flag, and commit count.</param>
    /// <param name="cancellationToken">Token that fires if the overall operation is cancelled.</param>
    Task<ApprovalResult> ApproveWriteRemoteAsync(WriteRemoteContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Called before an operation that will permanently discard working-tree changes
    /// (restore working-tree files, reset --hard, merge --abort).
    /// </summary>
    /// <param name="context">Files that will be overwritten or deleted.</param>
    /// <param name="cancellationToken">Token that fires if the overall operation is cancelled.</param>
    Task<ApprovalResult> ApproveDiscardLocalChangesAsync(DiscardChangesContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Called before an operation that will make local (unpushed) commits permanently unreachable
    /// (branch -d/-D on an unmerged branch, reset that moves HEAD behind unpushed commits).
    /// </summary>
    /// <param name="context">Branch name and list of commits that will be lost.</param>
    /// <param name="cancellationToken">Token that fires if the overall operation is cancelled.</param>
    Task<ApprovalResult> ApproveUnpushedCommitLossAsync(UnpushedCommitLossContext context, CancellationToken cancellationToken);

    /// <summary>
    /// Called before rewriting history that has already been pushed to a remote
    /// (commit --amend when HEAD is pushed, push --force).
    /// </summary>
    /// <param name="context">Branch, commits affected, and whether a remote push is involved.</param>
    /// <param name="cancellationToken">Token that fires if the overall operation is cancelled.</param>
    Task<ApprovalResult> ApproveHistoryRewriteAsync(HistoryRewriteContext context, CancellationToken cancellationToken);
}
