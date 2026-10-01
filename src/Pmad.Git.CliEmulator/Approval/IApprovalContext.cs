namespace Pmad.Git.CliEmulator.Approval;

/// <summary>
/// Base interface for approval context types passed to <see cref="IUserApproval"/> methods.
/// </summary>
public interface IApprovalContext
{
    /// <summary>Gets the Git operation name requesting approval.</summary>
    string Operation { get; }
}