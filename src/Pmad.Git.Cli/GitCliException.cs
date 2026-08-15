namespace Pmad.Git.Cli;

[Serializable]
public class GitCliException : Exception
{
    
    public GitCliException()
    {

    }

    public GitCliException(string[] arguments, int exitCode, string stderr) 
        : base($"git failed with exit code {exitCode} :{Environment.NewLine}{stderr}{Environment.NewLine}{Environment.NewLine}Arguments:{Environment.NewLine}{string.Join(Environment.NewLine, arguments)}")
    {
        Arguments = arguments;
        ExitCode = exitCode;
        StdErr = stderr;
    }

    public string[]? Arguments { get; }

    public int ExitCode { get; }

    public string? StdErr { get; }
}