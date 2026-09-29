using Pmad.Git.CliEmulator;
using Pmad.Git.CliEmulator.Approval;

namespace Pmad.Git.CliEmulator.Test.Fakes;

public class TestUserApproval : IUserApproval
{
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;

    public bool AllowReadRemote { get; set; } = true;
    public bool AllowWriteRemote { get; set; } = true;
    public bool AllowDiscardLocalChanges { get; set; } = true;
    public bool AllowUnpushedCommitLoss { get; set; } = true;
    public bool AllowHistoryRewrite { get; set; } = true;

    public List<ReadRemoteContext> ReadRemoteCalls { get; } = new();
    public List<WriteRemoteContext> WriteRemoteCalls { get; } = new();
    public List<DiscardChangesContext> DiscardLocalChangesCalls { get; } = new();
    public List<UnpushedCommitLossContext> UnpushedCommitLossCalls { get; } = new();
    public List<HistoryRewriteContext> HistoryRewriteCalls { get; } = new();

    public Task<bool> ApproveReadRemoteAsync(ReadRemoteContext context)
    {
        ReadRemoteCalls.Add(context);
        return Task.FromResult(AllowReadRemote);
    }

    public Task<bool> ApproveWriteRemoteAsync(WriteRemoteContext context)
    {
        WriteRemoteCalls.Add(context);
        return Task.FromResult(AllowWriteRemote);
    }

    public Task<bool> ApproveDiscardLocalChangesAsync(DiscardChangesContext context)
    {
        DiscardLocalChangesCalls.Add(context);
        return Task.FromResult(AllowDiscardLocalChanges);
    }

    public Task<bool> ApproveUnpushedCommitLossAsync(UnpushedCommitLossContext context)
    {
        UnpushedCommitLossCalls.Add(context);
        return Task.FromResult(AllowUnpushedCommitLoss);
    }

    public Task<bool> ApproveHistoryRewriteAsync(HistoryRewriteContext context)
    {
        HistoryRewriteCalls.Add(context);
        return Task.FromResult(AllowHistoryRewrite);
    }
}
