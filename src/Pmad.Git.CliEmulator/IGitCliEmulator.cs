namespace Pmad.Git.CliEmulator;

/// <summary>
/// Defines an interface for a managed Git CLI emulator that dispatches git-like commands
/// </summary>
public interface IGitCliEmulator : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Invokes a git command and returns the captured output.
    /// </summary>
    /// <param name="args">Command-line arguments (e.g. <c>["status"]</c>, <c>["commit", "-m", "msg"]</c>).</param>
    /// <param name="userApproval">
    /// Callback that gates destructive or network operations.
    /// Use <see langword="null"/> to auto-approve all operations.
    /// </param>
    /// <param name="cancellationToken">
    /// Token used to cancel the overall operation.
    /// This token is also forwarded to each <see cref="IUserApproval"/> method so that approval UIs
    /// can dismiss themselves if the operation is cancelled from outside.
    /// </param>
    Task<GitCliResponse> InvokeAsync(string[] args, IUserApproval? userApproval = null, CancellationToken cancellationToken = default);
}