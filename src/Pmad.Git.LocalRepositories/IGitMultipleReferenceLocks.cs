namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents a collection of locks for multiple Git references.
/// This interface ensures that multiple references can be updated atomically while preventing concurrent modifications.
/// </summary>
public interface IGitMultipleReferenceLocks : IDisposable
{
    /// <summary>
    /// Writes or overwrites the value of a reference file with validation.
    /// This method validates that the expected old value matches the current value before updating.
    /// </summary>
    /// <param name="referencePath">Fully qualified reference path (for example refs/heads/main).</param>
    /// <param name="expectedOldValue">Expected current hash of the reference, or null if reference should not exist.</param>
    /// <param name="newValue">New hash to persist, or null to delete the reference.</param>
    /// <param name="cancellationToken">Token used to cancel the async operation.</param>
    /// <exception cref="InvalidOperationException">Thrown when the expected old value doesn't match the current value.</exception>
    Task WriteReferenceWithValidationAsync(
        string referencePath,
        GitHash? expectedOldValue,
        GitHash? newValue,
        CancellationToken cancellationToken = default);
}