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
- `CheckoutAsync(branchName, createNew, startPoint)` checks out an existing branch, or creates and checks out a new one. Pass `updateWorkingTree: false` to only move `HEAD` (via `git symbolic-ref`) without touching the working tree, when switching branches on a repository whose files are not otherwise used.
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

## Synchronizing a repository automatically with `GitRepositorySynchronizer`

`GitRepositorySynchronizer` builds on top of `GitCliRepository` to keep a local repository continuously synchronized with a remote, without requiring the caller to manually drive fetch/pull/push calls:

- **Local-to-remote** synchronization is debounced: when the local repository changes (a commit, a Smart HTTP push handled by `Pmad.Git.HttpServer`, ...), a push is scheduled after `GitSyncOptions.PushDebounceDelay` (5 minutes by default) of inactivity. Repeated changes reset the delay, so a burst of local commits results in a single push.
- **Remote-to-local** synchronization runs periodically every `GitSyncOptions.PullInterval` (1 hour by default), and can also be triggered on demand, for example from a webhook endpoint called by the remote, using `TriggerRemoteSyncAsync`.
- **Conflict handling**: when a pull results in a merge conflict, the synchronizer switches to `GitSyncState.Conflict`, exposes the conflicted files through `Conflict`, and stops automatic synchronization until the conflict is resolved (or discarded) — for example from a web UI.

When created from an `IGitRepository` (via the constructor or `CreateSynchronizer` extension method), the synchronizer automatically subscribes to `IGitRepositoryCacheInvalidator.Changed` to detect local changes, so no manual wiring is needed as long as all local mutations go through that repository instance. Since the synchronizer's own push/pull/merge operations also raise `Changed`, those self-triggered notifications are ignored so completing a push or pull never triggers an endless synchronization loop.

### Quick start

```csharp
using Pmad.Git.LocalRepositories;
using Pmad.Git.Cli;

var repository = GitRepository.Open("/path/to/repo");

// Creates the synchronizer and starts the periodic pull loop
await using var synchronizer = repository.CreateSynchronizer(new GitSyncOptions
{
    Remote = "origin",
    Branch = "main",
    PushDebounceDelay = TimeSpan.FromMinutes(5),
    PullInterval = TimeSpan.FromHours(1),
});

// Local changes made through 'repository' are detected automatically and pushed after the debounce delay.
// A manual remote sync can be triggered at any time, e.g. from a webhook endpoint:
await synchronizer.TriggerRemoteSyncAsync();
```

### Handling conflicts

```csharp
if (synchronizer.State == GitSyncState.Conflict)
{
    foreach (var file in synchronizer.Conflict!.ConflictedFiles)
    {
        // Resolve the file manually (e.g. through a web UI), then stage it:
        await synchronizer.ResolveConflictAsync(file);
    }

    // Once every conflicted file has been resolved, complete the merge and resume synchronization:
    await synchronizer.CompleteConflictResolutionAsync();

    // Alternatively, discard the merge and resume synchronization from the pre-merge state:
    // await synchronizer.AbortConflictResolutionAsync();
}
```

### Notes on testability

`GitSyncOptions` exposes an internal `IGitRunner` (settable directly, or implicitly via `GitCliPath`) so that unit tests can inject a fake runner instead of shelling out to a real `git` executable. `GitCliRepository` has a matching internal constructor accepting a custom `IGitRunner` for the same purpose.

## Using alongside `Pmad.Git.LocalRepositories`

`GitCliRepository` (this library) and `GitRepository` (from `Pmad.Git.LocalRepositories`) can safely operate on the same repository directory within the same process if they share the same lock manager, and `GitRepository` needs to be told to invalidate its cached references/objects after `GitCliRepository` writes to the repository. The easiest way to combine both is to pass the `GitRepository` instance directly:

```csharp
using Pmad.Git.LocalRepositories;
using Pmad.Git.Cli;

var repository = GitRepository.Open("/path/to/repo");
var cliRepository = new GitCliRepository("/path/to/repo", repository);

// repository's caches are automatically invalidated after this call
await cliRepository.PullAsync();

var head = await repository.GetCommitAsync();
```

This constructor uses `repository.LockManager` for synchronization and `repository` itself (which implements `IGitRepositoryCacheInvalidator`) to clear caches, so you never have to remember to call `InvalidateCaches()` manually.

If you only need one of the two behaviors, or want more control, use the other constructors instead:

```csharp
// Lock synchronization only, no automatic cache invalidation
var cliRepository = new GitCliRepository("/path/to/repo", repository.LockManager);

// Lock synchronization and a custom (or explicit) cache invalidator
var cliRepository = new GitCliRepository("/path/to/repo", repository.LockManager, repository);
```

In that case, remember to call `repository.InvalidateCaches()` yourself after any `GitCliRepository` write operation, otherwise `GitRepository` may keep returning stale references, commits or trees.

When a shared lock manager is provided, every write operation performed by `GitCliRepository` (fetch, pull, push, checkout, merge, branch management, ...) acquires the same global lock used by `GitRepository` for reference writes, preventing interleaved writes to refs/objects between the two. Cache invalidation (automatic or manual) always happens while that lock is still held, so no reader can observe stale data in between.

This only protects concurrent writes performed by the current process. It does not protect against a `git` process started independently (e.g. from a terminal or another application), nor against concurrent writes across multiple processes.

Additionally, `GitRepository` never touches the working tree, while `git` CLI commands like `checkout`, `merge` and `pull` update it by default. When this is unnecessary (e.g. a server-side repository whose working tree files are not consumed), prefer `CheckoutAsync(branchName, updateWorkingTree: false)` to move `HEAD` without writing files, and prefer `FetchAsync` (which never touches the working tree) over `PullAsync` when you intend to integrate changes through `Pmad.Git.LocalRepositories` instead of `MergeAsync`.
