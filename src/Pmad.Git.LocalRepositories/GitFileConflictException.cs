namespace Pmad.Git.LocalRepositories;

/// <summary>
/// The exception that is thrown when a file conflict occurs during a Git operation.
/// </summary>
/// <remarks>
/// This exception indicates that a file could not be processed due to a conflict detected by the Git client,
/// for example when using hash-based optimistic locking for concurrent file updates or during other operations
/// that detect conflicting changes. The file path associated with the conflict is available in the
/// <see cref="FilePath" /> property.
/// </remarks>
public sealed class GitFileConflictException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the GitFileConflictException class with a specified error message and the path of
    /// the file that caused the conflict.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="filePath">The path to the file that is involved in the conflict.</param>
    public GitFileConflictException(string message, string filePath)
        : base(message)
    {
        FilePath = filePath;
    }

    /// <summary>
    /// Gets the path to the file that is involved in the conflict.
    /// </summary>
    public string FilePath { get; }
}
