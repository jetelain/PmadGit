# Pmad.Git.Cli

`Pmad.Git.Cli` is a lightweight .NET 8 library that wraps calls to the Git command-line interface (CLI) for managing Git repositories. It provides a simple API for executing Git commands and retrieving their output, making it easier to integrate Git functionality into .NET applications without directly invoking shell commands.

It is intended to allow more advanced Git operations that are not supported by `Pmad.Git.LocalRepositories`, such as pushing and pulling from remote repositories, managing branches, and handling merge conflicts.

`GitCliRepository` exposes low level primitives only, so that higher level applications (for example a web application synchronizing an internal and an external repository) can implement their own pull/push/merge workflow and let users resolve conflicts manually:

- `FetchAsync` / `PullAsync` / `PushAsync` to synchronize with a remote.
- `GetCurrentBranchAsync`, `GetBranchesAsync`, `CreateBranchAsync`, `CheckoutAsync`, `DeleteBranchAsync`, `RenameBranchAsync` to manage branches.
- `MergeAsync`, `IsMergeInProgressAsync`, `GetConflictedFilesAsync`, `ResolveConflictAsync`, `ContinueMergeAsync`, `AbortMergeAsync` to merge branches and manually resolve conflicts.

## Installation

Add a project reference to `Pmad.Git.Cli` or publish it as a package and reference it like any other NuGet dependency. The library targets .NET 8 and requires the `git` executable to be available (either on the `PATH` or by specifying an explicit path).

## Quick Start

```csharp
using Pmad.Git.Cli;

var repository = new GitCliRepository("/path/to/repo");

// Synchronize with the remote
await repository.FetchAsync();
var pullResult = await repository.PullAsync();
if (pullResult.HasConflicts)
{
    Console.WriteLine($"Conflicts: {string.Join(", ", pullResult.ConflictedFiles)}");
}

await repository.PushAsync();

// Run an arbitrary git command
var log = await repository.RunAsync("log", "--oneline", "-5");
Console.WriteLine(log);
```

### Running raw commands
- `RunAsync(arguments)` / `RunAsync(cancellationToken, arguments)` run any git command in the repository and return its standard output, throwing a `GitCliException` on failure.

### Synchronizing with a remote
- `FetchAsync(remote, branch, prune)` downloads objects and refs from a remote without integrating them.
- `PullAsync(remote, branch, rebase)` fetches and merges (or rebases) changes from a remote, returning a `GitMergeResult`.
- `PushAsync(remote, branch, force, setUpstream)` pushes local commits to a remote. `force` uses `--force-with-lease` to avoid clobbering unseen remote changes.

### Managing branches
- `GetCurrentBranchAsync()` returns the name of the currently checked out branch.
- `GetBranchesAsync(includeRemote)` lists local branches, optionally including remote-tracking branches.
- `CreateBranchAsync(branchName, startPoint)` creates a branch without checking it out.
- `CheckoutAsync(branchName, createNew, startPoint)` checks out an existing branch, or creates and checks out a new one.
- `DeleteBranchAsync(branchName, force)` deletes a local branch.
- `RenameBranchAsync(oldName, newName)` renames a local branch.

### Merging and resolving conflicts
- `MergeAsync(branch)` merges a branch into the current one and returns a `GitMergeResult` instead of throwing when conflicts occur.
- `GitMergeResult.IsSuccess` / `HasConflicts` / `ConflictedFiles` describe the outcome of a merge or pull.
- `IsMergeInProgressAsync()` indicates whether a merge is currently waiting for conflict resolution.
- `GetConflictedFilesAsync()` lists the relative paths of files currently in conflict.
- `ResolveConflictAsync(relativeFilePath)` stages a file once its conflict has been resolved (equivalent to `git add`).
- `ContinueMergeAsync(commitMessage)` completes the merge after all conflicts have been resolved.
- `AbortMergeAsync()` cancels an in-progress merge and restores the pre-merge state.

## Limitations & roadmap
- Requires the `git` executable; it is not a managed re-implementation of Git.
- Authentication for remote operations (credentials, SSH keys) relies on the local Git/OS configuration and is not managed by this library.
