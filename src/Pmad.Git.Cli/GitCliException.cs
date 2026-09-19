namespace Pmad.Git.Cli;

/// <summary>
/// Exception thrown when a Git CLI command exits with a non-zero status code.
/// </summary>
public class GitCliException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitCliException"/> class.
    /// </summary>
    public GitCliException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitCliException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public GitCliException(string? message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitCliException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public GitCliException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitCliException"/> class with details of the failed git execution.
    /// </summary>
    /// <param name="arguments">Arguments that were passed to the git executable.</param>
    /// <param name="exitCode">Exit code returned by the git executable.</param>
    /// <param name="stderr">Standard error output produced by the git executable.</param>
    public GitCliException(string[] arguments, int exitCode, string stderr) 
        : base($"git failed with exit code {exitCode} :{Environment.NewLine}{stderr}{Environment.NewLine}{Environment.NewLine}Arguments:{Environment.NewLine}{string.Join(Environment.NewLine, arguments)}")
    {
        Arguments = arguments;
        ExitCode = exitCode;
        StdErr = stderr;
    }

    /// <summary>
    /// Gets the arguments passed to the git executable, if available.
    /// </summary>
    public string[]? Arguments { get; }

    /// <summary>
    /// Gets the exit code returned by the git command.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// Gets the standard error output produced by the git command.
    /// </summary>
    public string? StdErr { get; }
}