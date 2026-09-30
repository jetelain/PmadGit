namespace Pmad.Git.CliEmulator;

/// <summary>
/// The outcome returned by an <see cref="IUserApproval"/> callback.
/// </summary>
public enum ApprovalResult
{
    /// <summary>The user approved the operation; it may proceed.</summary>
    Approved,

    /// <summary>
    /// The user explicitly denied the operation.
    /// The emulator returns exit code 130 with a denial message.
    /// </summary>
    Denied,

    /// <summary>
    /// The user (or the approval UI) cancelled the overall operation
    /// (e.g. closed a dialog without deciding).
    /// The emulator propagates this as <see cref="OperationCanceledException"/>,
    /// which also results in exit code 130.
    /// </summary>
    Cancelled,
}
