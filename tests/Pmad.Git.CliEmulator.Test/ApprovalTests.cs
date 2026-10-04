using Pmad.Git.CliEmulator;
using Pmad.Git.CliEmulator.Approval;

namespace Pmad.Git.CliEmulator.Test;

public class ApprovalTests
{
    [Fact]
    public async Task AutoApproval_ApproveAllOperations_ReturnsApproved()
    {
        var approval = AutoApproval.Instance;
        var discard = new DiscardChangesContext { Operation = "test", AffectedFiles = ["file.txt"] };
        var rewrite = new HistoryRewriteContext { Operation = "test", BranchName = "branch", AffectedCommits = [] };
        var readRemote = new ReadRemoteContext { Operation = "test", RemoteName = "origin", RemoteUrl = "https://example.com" };
        var unpushed = new UnpushedCommitLossContext { Operation = "test", BranchName = "branch", CommitsToLose = [] };
        var writeRemote = new WriteRemoteContext { RemoteName = "origin", RemoteUrl = "https://example.com" };

        Assert.Equal(ApprovalResult.Approved, await approval.ApproveDiscardLocalChangesAsync(discard, CancellationToken.None));
        Assert.Equal(ApprovalResult.Approved, await approval.ApproveHistoryRewriteAsync(rewrite, CancellationToken.None));
        Assert.Equal(ApprovalResult.Approved, await approval.ApproveReadRemoteAsync(readRemote, CancellationToken.None));
        Assert.Equal(ApprovalResult.Approved, await approval.ApproveUnpushedCommitLossAsync(unpushed, CancellationToken.None));
        Assert.Equal(ApprovalResult.Approved, await approval.ApproveWriteRemoteAsync(writeRemote, CancellationToken.None));
    }

    [Fact]
    public async Task DenyApproval_DenyAllOperations_ReturnsDenied()
    {
        var approval = DenyApproval.Instance;
        var discard = new DiscardChangesContext { Operation = "test", AffectedFiles = ["file.txt"] };
        var rewrite = new HistoryRewriteContext { Operation = "test", BranchName = "branch", AffectedCommits = [] };
        var readRemote = new ReadRemoteContext { Operation = "test", RemoteName = "origin", RemoteUrl = "https://example.com" };
        var unpushed = new UnpushedCommitLossContext { Operation = "test", BranchName = "branch", CommitsToLose = [] };
        var writeRemote = new WriteRemoteContext { RemoteName = "origin", RemoteUrl = "https://example.com" };

        Assert.Equal(ApprovalResult.Denied, await approval.ApproveDiscardLocalChangesAsync(discard, CancellationToken.None));
        Assert.Equal(ApprovalResult.Denied, await approval.ApproveHistoryRewriteAsync(rewrite, CancellationToken.None));
        Assert.Equal(ApprovalResult.Denied, await approval.ApproveReadRemoteAsync(readRemote, CancellationToken.None));
        Assert.Equal(ApprovalResult.Denied, await approval.ApproveUnpushedCommitLossAsync(unpushed, CancellationToken.None));
        Assert.Equal(ApprovalResult.Denied, await approval.ApproveWriteRemoteAsync(writeRemote, CancellationToken.None));
    }
}
