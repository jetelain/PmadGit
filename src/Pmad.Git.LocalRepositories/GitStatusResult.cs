namespace Pmad.Git.LocalRepositories;

/// <summary>
/// Encapsulates the overall status of a Git working tree and staging area.
/// </summary>
public sealed class GitStatusResult
{
    /// <summary>
    /// Gets all status entries (changed files, and optionally clean files if requested).
    /// </summary>
    public IReadOnlyList<GitStatusEntry> Entries { get; }

    /// <summary>
    /// Gets a value indicating whether the working tree and staging area are completely clean.
    /// </summary>
    public bool IsClean => Entries.All(e => e.IsClean);

    /// <summary>
    /// Gets all entries that have staged changes (new, modified, or deleted).
    /// </summary>
    public IReadOnlyList<GitStatusEntry> StagedEntries { get; }

    /// <summary>
    /// Gets all entries that are tracked and modified in the working tree.
    /// </summary>
    public IReadOnlyList<GitStatusEntry> ModifiedEntries { get; }

    /// <summary>
    /// Gets all entries that are untracked in the working tree.
    /// </summary>
    public IReadOnlyList<GitStatusEntry> UntrackedEntries { get; }

    /// <summary>
    /// Gets all entries that have been deleted from the working tree.
    /// </summary>
    public IReadOnlyList<GitStatusEntry> DeletedEntries { get; }

    /// <summary>
    /// Gets all entries that have unresolved merge conflicts.
    /// </summary>
    public IReadOnlyList<GitStatusEntry> ConflictedEntries { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitStatusResult"/> class.
    /// </summary>
    /// <param name="entries">The collection of status entries.</param>
    public GitStatusResult(IReadOnlyList<GitStatusEntry> entries)
    {
        Entries = entries ?? Array.Empty<GitStatusEntry>();
        StagedEntries = Entries.Where(e => e.IsStaged).ToList();
        ModifiedEntries = Entries.Where(e => e.WorkingTreeStatus == GitFileStatus.Modified).ToList();
        UntrackedEntries = Entries.Where(e => e.WorkingTreeStatus == GitFileStatus.Untracked).ToList();
        DeletedEntries = Entries.Where(e => e.WorkingTreeStatus == GitFileStatus.Deleted).ToList();
        ConflictedEntries = Entries.Where(e => e.IsConflicted).ToList();
    }

    /// <summary>
    /// Finds a status entry by its repository-relative path.
    /// </summary>
    /// <param name="path">Repository-relative file path.</param>
    /// <returns>The matching <see cref="GitStatusEntry"/>, or null if not found.</returns>
    public GitStatusEntry? FindEntry(string path)
    {
        var normalized = path.Replace('\\', '/');
        return Entries.FirstOrDefault(e => e.Path.Equals(normalized, StringComparison.Ordinal));
    }
}
