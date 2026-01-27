namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Moves or renames an existing file in the tree.
/// </summary>
public sealed class MoveFileOperation : GitCommitOperation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MoveFileOperation"/> class.
    /// </summary>
    /// <param name="sourcePath">The source path of the file to move or rename.</param>
    /// <param name="destinationPath">The destination path for the file.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="sourcePath"/> or <paramref name="destinationPath"/> is null, empty, or whitespace.</exception>
    public MoveFileOperation(string sourcePath, string destinationPath) : base(sourcePath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Destination path cannot be empty", nameof(destinationPath));
        }

        DestinationPath = destinationPath;
    }

    /// <summary>
    /// Gets the destination path for the move.
    /// </summary>
    public string DestinationPath { get; }
}
