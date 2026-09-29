namespace Pmad.Git.CliEmulator;

/// <summary>
/// Thrown when a <see cref="IUserApproval"/> callback returns <see langword="false"/>,
/// indicating the user denied the requested operation.
/// </summary>
public sealed class GitCliDeniedException : Exception
{
    /// <summary>Gets the name of the operation that was denied.</summary>
    public string Operation { get; }

    /// <summary>Initializes a new instance of <see cref="GitCliDeniedException"/>.</summary>
    /// <param name="operation">Human-readable name of the denied operation.</param>
    public GitCliDeniedException(string operation)
        : base($"Operation '{operation}' was denied by the user.")
    {
        Operation = operation;
    }
}
