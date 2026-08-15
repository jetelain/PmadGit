using System.Text;

namespace Pmad.Git.Cli;

internal sealed class GitResponse
{
    private readonly int exitCode;
    private readonly StringBuilder stdout;
    private readonly StringBuilder stderr;
    private readonly string[] arguments;

    public GitResponse(int exitCode, StringBuilder stdout, StringBuilder stderr, string[] arguments)
    {
        this.exitCode = exitCode;
        this.stdout = stdout;
        this.stderr = stderr;
        this.arguments = arguments;
    }

    public void EnsureSuccess()
    {
        if (exitCode != 0)
        {
            throw new GitCliException(arguments, exitCode, StdErr);
        }
    }

    public int ExitCode => exitCode;

    public string[] Arguments => arguments;

    public string StdOut => stdout.ToString();

    public string StdErr => stderr.ToString();
}