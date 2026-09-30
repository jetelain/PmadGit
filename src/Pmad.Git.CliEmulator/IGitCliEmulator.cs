namespace Pmad.Git.CliEmulator;

/// <summary>
/// Defines an interface for a managed Git CLI emulator that dispatches git-like commands
/// </summary>
public interface IGitCliEmulator : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Invokes a git command and returns the captured output.
    /// </summary>
    /// <remarks>
    /// Cancellation is supported via the <see cref="IUserApproval.CancellationToken"/> property.
    /// </remarks>
    Task<GitCliResponse> InvokeAsync(string[] args, IUserApproval userApproval);
}