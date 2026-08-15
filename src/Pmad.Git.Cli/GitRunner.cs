using System.Diagnostics;
using System.Text;

namespace Pmad.Git.Cli;

internal class GitRunner : IGitRunner
{
    /// <summary>
    /// Path to the Git CLI executable.
    /// </summary>
    public string GitCliPath { get; }

    public GitRunner(string gitCliPath = "git")
    {
        GitCliPath = gitCliPath;
    }

    public async Task<GitResponse> RunGit(string workingDirectory, string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(GitCliPath, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start git process");
        process.OutputDataReceived += (_, e) => { stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { stderr.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();

        return new GitResponse(process.ExitCode, stdout, stderr, arguments);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between the check and the kill call.
        }
    }
}
