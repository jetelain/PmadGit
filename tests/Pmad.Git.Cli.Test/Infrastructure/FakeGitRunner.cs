using System.Text;

namespace Pmad.Git.Cli.Test.Infrastructure;

/// <summary>
/// A configurable in-memory <see cref="IGitRunner"/> used to unit test <see cref="GitCliRepository"/>
/// without invoking the real <c>git</c> executable.
/// </summary>
internal sealed class FakeGitRunner : IGitRunner
{
    private readonly Queue<(int ExitCode, string StdOut, string StdErr)> _responses = new();

    public string GitCliPath { get; set; } = "git";

    public List<string[]> Calls { get; } = new();

    public List<string> WorkingDirectories { get; } = new();

    /// <summary>
    /// Queues a response returned by the next call to <see cref="RunGit"/>.
    /// </summary>
    public FakeGitRunner Enqueue(int exitCode, string stdout = "", string stderr = "")
    {
        _responses.Enqueue((exitCode, stdout, stderr));
        return this;
    }

    public Task<GitResponse> RunGit(string workingDirectory, string[] arguments, CancellationToken cancellationToken)
    {
        Calls.Add(arguments);
        WorkingDirectories.Add(workingDirectory);

        var (exitCode, stdout, stderr) = _responses.Count > 0 ? _responses.Dequeue() : (0, string.Empty, string.Empty);

        return Task.FromResult(new GitResponse(exitCode, new StringBuilder(stdout), new StringBuilder(stderr), arguments));
    }
}
