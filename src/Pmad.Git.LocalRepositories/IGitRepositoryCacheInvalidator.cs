namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Represents a component that caches git metadata (references, objects, commits, trees, ...)
/// and needs to be notified when the underlying repository is modified by another component,
/// such as a CLI-based wrapper, operating on the same directory.
/// </summary>
public interface IGitRepositoryCacheInvalidator
{
    /// <summary>
    /// Clears cached git metadata so subsequent operations reflect the current repository state.
    /// </summary>
    /// <param name="clearAllData">When <see langword="true"/>, clears all cached data including structural metadata
    /// (e.g. pack index). When <see langword="false"/>, only volatile data such as references and loose objects are cleared.</param>
    void InvalidateCaches(bool clearAllData = false);
}
