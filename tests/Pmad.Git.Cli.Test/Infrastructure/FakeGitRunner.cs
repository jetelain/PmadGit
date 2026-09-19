using System.Text;

namespace Pmad.Git.Cli.Test.Infrastructure;

/// <summary>
/// A configurable in-memory <see cref="IGitRunner"/> used to unit test <see cref="GitCliRepository"/>
/// without invoking the real <c>git</c> executable.
/// </summary>
internal sealed class FakeGitRunner : IGitRunner
{
    private readonly Queue<(int ExitCode, string StdOut, string StdErr, BlockingGitCall? Blocking)> _responses = new();

    public string GitCliPath { get; set; } = "git";

    public List<string[]> Calls { get; } = new();

    public List<string> WorkingDirectories { get; } = new();

    /// <summary>
    /// Queues a response returned by the next call to <see cref="RunGit"/>.
    /// </summary>
    public FakeGitRunner Enqueue(int exitCode, string stdout = "", string stderr = "")
    {
        _responses.Enqueue((exitCode, stdout, stderr, null));
        return this;
    }

    /// <summary>
    /// Queues a call to <see cref="RunGit"/> that blocks until <see cref="BlockingGitCall.Complete"/>
    /// is invoked, or the operation's <see cref="CancellationToken"/> is cancelled. Useful to
    /// simulate a slow git operation that keeps a synchronizer's gate held, or to test cancellation
    /// of an in-flight operation.
    /// </summary>
    public BlockingGitCall EnqueueBlocking()
    {
        var blocking = new BlockingGitCall();
        _responses.Enqueue((0, string.Empty, string.Empty, blocking));
        return blocking;
    }

    public async Task<GitResponse> RunGit(string workingDirectory, string[] arguments, CancellationToken cancellationToken)
    {
        Calls.Add(arguments);
        WorkingDirectories.Add(workingDirectory);

        var (exitCode, stdout, stderr, blocking) = _responses.Count > 0 ? _responses.Dequeue() : (0, string.Empty, string.Empty, null);

        if (blocking != null)
        {
            (exitCode, stdout, stderr) = await blocking.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return new GitResponse(exitCode, new StringBuilder(stdout), new StringBuilder(stderr), arguments);
    }
}

/// <summary>
/// Controls the completion of a <see cref="FakeGitRunner"/> call queued via
/// <see cref="FakeGitRunner.EnqueueBlocking"/>.
/// </summary>
internal sealed class BlockingGitCall
{
    private readonly TaskCompletionSource<(int ExitCode, string StdOut, string StdErr)> _tcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Lets the blocked <see cref="FakeGitRunner.RunGit"/> call return with the given result.
    /// </summary>
    public void Complete(int exitCode = 0, string stdout = "", string stderr = "")
    {
        _tcs.TrySetResult((exitCode, stdout, stderr));
    }

    internal async Task<(int ExitCode, string StdOut, string StdErr)> WaitAsync(CancellationToken cancellationToken)
    {
        using var registration = cancellationToken.Register(() => _tcs.TrySetCanceled(cancellationToken));
        return await _tcs.Task.ConfigureAwait(false);
    }
}
