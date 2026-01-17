namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Moves or renames an existing file in the tree.
/// </summary>
public sealed class MoveFileOperation : GitCommitOperation
{
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
