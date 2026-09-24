using Pmad.Git.LocalRepositories;

namespace Pmad.Git.RemoteClient;

/// <summary>
/// Represents a reference update command for a push operation.
/// </summary>
/// <param name="OldValue">The expected current value of the reference on the remote, or <see langword="null"/> for new references.</param>
/// <param name="NewValue">The new value to assign to the reference, or <see langword="null"/> to delete the reference.</param>
/// <param name="RefName">The full name of the reference (e.g. <c>refs/heads/main</c>).</param>
public sealed record GitRefUpdateCommand(GitHash? OldValue, GitHash? NewValue, string RefName);

