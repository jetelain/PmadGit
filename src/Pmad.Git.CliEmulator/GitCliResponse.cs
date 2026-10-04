namespace Pmad.Git.CliEmulator;

/// <summary>
/// Represents the result of a git CLI emulation invocation.
/// </summary>
public sealed class GitCliResponse
{
    /// <summary>Gets the process exit code (0 = success, 1 = error, 128 = remote/fatal, 130 = denied/cancelled).</summary>
    public int ExitCode { get; init; }

    /// <summary>Gets the standard output text.</summary>
    public string StdOut { get; init; } = string.Empty;

    /// <summary>Gets the standard error text.</summary>
    public string StdErr { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the command succeeded (exit code 0).</summary>
    public bool IsSuccess => ExitCode == 0;
}
