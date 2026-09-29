using Pmad.Git.CliEmulator.Approval;

namespace Pmad.Git.CliEmulator;

/// <summary>
/// Callback interface that the host application implements to gate destructive or remote operations.
/// All methods return <see langword="true"/> to allow, <see langword="false"/> to deny.
/// Returning <see langword="false"/> causes a <see cref="GitCliDeniedException"/> to be thrown.
/// Throwing <see cref="OperationCanceledException"/> (or cancelling the token) propagates normally.
/// </summary>
public interface IUserApproval
{
    /// <summary>Token used to cancel any pending approval dialog and the overall operation.</summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Called before a network read operation (fetch, pull).
    /// </summary>
    /// <param name="context">Details about the remote and branch being fetched.</param>
    Task<bool> ApproveReadRemoteAsync(ReadRemoteContext context);

    /// <summary>
    /// Called before a network write operation (push).
    /// </summary>
    /// <param name="context">Details about the remote, branch, force flag, and commit count.</param>
    Task<bool> ApproveWriteRemoteAsync(WriteRemoteContext context);

    /// <summary>
    /// Called before an operation that will permanently discard working-tree changes
    /// (restore working-tree files, reset --hard, merge --abort).
    /// </summary>
    /// <param name="context">Files that will be overwritten or deleted.</param>
    Task<bool> ApproveDiscardLocalChangesAsync(DiscardChangesContext context);

    /// <summary>
    /// Called before an operation that will make local (unpushed) commits permanently unreachable
    /// (branch -d/-D on an unmerged branch, reset that moves HEAD behind unpushed commits).
    /// </summary>
    /// <param name="context">Branch name and list of commits that will be lost.</param>
    Task<bool> ApproveUnpushedCommitLossAsync(UnpushedCommitLossContext context);

    /// <summary>
    /// Called before rewriting history that has already been pushed to a remote
    /// (commit --amend when HEAD is pushed, push --force).
    /// </summary>
    /// <param name="context">Branch, commits affected, and whether a remote push is involved.</param>
    Task<bool> ApproveHistoryRewriteAsync(HistoryRewriteContext context);
}
