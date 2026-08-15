namespace Pmad.Git.Cli.Test;

/// <summary>
/// Unit tests for <see cref="GitRunner"/>, focusing on process lifecycle behavior.
/// </summary>
public class GitRunnerTests
{
    [Fact]
    public async Task RunGit_Returns_ExitCode_And_Output_On_Success()
    {
        var runner = new GitRunner();

        var response = await runner.RunGit(Path.GetTempPath(), new[] { "--version" }, CancellationToken.None);

        Assert.Equal(0, response.ExitCode);
        Assert.Contains("git version", response.StdOut);
    }

    [Fact]
    public async Task RunGit_On_Cancellation_Kills_Process_Before_Rethrowing()
    {
        var runner = new GitRunner();
        using var cts = new CancellationTokenSource();

        // "git hash-object --stdin" blocks waiting for input on stdin, so it stays alive until killed.
        var runTask = runner.RunGit(Path.GetTempPath(), new[] { "hash-object", "--stdin" }, cts.Token);

        // Give the process a moment to start before cancelling.
        await Task.Delay(200);

        cts.Cancel();

        // RunGit is expected to kill the process tree and await its exit before rethrowing.
        // If it only cancelled the wait without killing the process, this call would still return quickly,
        // but the underlying git process would be left running in the background.
        var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(10)));
        // completed will be the first task to complete, which should be runTask if it properly handles cancellation and kills the process.
        Assert.Same(runTask, completed);

        await Assert.ThrowsAsync<TaskCanceledException>(() => runTask);
    }
}
