using Pmad.Git.CliEmulator;
using Pmad.Git.CliEmulator.Approval;

namespace Pmad.Git.CliEmulator.Test.Fakes;

public class TestUserApproval : IUserApproval
{
    public ApprovalResult ReadRemoteResult { get; set; } = ApprovalResult.Approved;
    public ApprovalResult WriteRemoteResult { get; set; } = ApprovalResult.Approved;
    public ApprovalResult DiscardLocalChangesResult { get; set; } = ApprovalResult.Approved;
    public ApprovalResult UnpushedCommitLossResult { get; set; } = ApprovalResult.Approved;
    public ApprovalResult HistoryRewriteResult { get; set; } = ApprovalResult.Approved;

    public List<ReadRemoteContext> ReadRemoteCalls { get; } = new();
    public List<WriteRemoteContext> WriteRemoteCalls { get; } = new();
    public List<DiscardChangesContext> DiscardLocalChangesCalls { get; } = new();
    public List<UnpushedCommitLossContext> UnpushedCommitLossCalls { get; } = new();
    public List<HistoryRewriteContext> HistoryRewriteCalls { get; } = new();

    public Task<ApprovalResult> ApproveReadRemoteAsync(ReadRemoteContext context, CancellationToken cancellationToken)
    {
        ReadRemoteCalls.Add(context);
        return Task.FromResult(ReadRemoteResult);
    }

    public Task<ApprovalResult> ApproveWriteRemoteAsync(WriteRemoteContext context, CancellationToken cancellationToken)
    {
        WriteRemoteCalls.Add(context);
        return Task.FromResult(WriteRemoteResult);
    }

    public Task<ApprovalResult> ApproveDiscardLocalChangesAsync(DiscardChangesContext context, CancellationToken cancellationToken)
    {
        DiscardLocalChangesCalls.Add(context);
        return Task.FromResult(DiscardLocalChangesResult);
    }

    public Task<ApprovalResult> ApproveUnpushedCommitLossAsync(UnpushedCommitLossContext context, CancellationToken cancellationToken)
    {
        UnpushedCommitLossCalls.Add(context);
        return Task.FromResult(UnpushedCommitLossResult);
    }

    public Task<ApprovalResult> ApproveHistoryRewriteAsync(HistoryRewriteContext context, CancellationToken cancellationToken)
    {
        HistoryRewriteCalls.Add(context);
        return Task.FromResult(HistoryRewriteResult);
    }
}
