namespace Pmad.Git.LocalRepositories.Diff;

/// <summary>
/// Represents an atomic change operation within a difference sequence.
/// </summary>
/// <typeparam name="T">The type of items being compared.</typeparam>
/// <param name="Type">The type of difference operation.</param>
/// <param name="Item">The item affected by this change.</param>
/// <param name="OldIndex">The zero-based index in the original sequence, or -1 if inserted.</param>
/// <param name="NewIndex">The zero-based index in the modified sequence, or -1 if deleted.</param>
public sealed record DiffChange<T>(DiffChangeType Type, T Item, int OldIndex, int NewIndex);

