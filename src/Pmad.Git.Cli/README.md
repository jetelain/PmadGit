# Pmad.Git.Cli

`Pmad.Git.Cli` is a lightweight .NET library that wraps calls to the Git command-line interface (`git`) for advanced operations not handled in-process, such as remote synchronization (push/pull/fetch), branch management, tracking status inspection, unified diff extraction, and merge conflict resolution.

It also provides `GitRepositorySynchronizer`, which automatically keeps a local repository synchronized with a remote using debounced push-on-change and periodic (or on-demand) pull, exposing a state machine to detect and resolve merge conflicts.

## Installation

Add a project reference to `Pmad.Git.Cli` or publish it as a package and reference it like any other NuGet dependency. The library targets .NET 8 and uses C# 12 features.

```bash
dotnet add package Pmad.Git.Cli
```

## Quick Start

```csharp
using Pmad.Git.Cli;
using Pmad.Git.LocalRepositories;

// Open an existing repository using GitCliRepository
var cliRepo = new GitCliRepository("/path/to/repo");

// Inspect upstream tracking status (ahead/behind commit counts)
var status = await cliRepo.GetTrackingStatusAsync();
Console.WriteLine($"Branch: {status.LocalBranch} -> {status.UpstreamBranch}");
Console.WriteLine($"Ahead: {status.AheadCount}, Behind: {status.BehindCount}, Synced: {status.IsSynchronized}");

// Check if a specific commit has been pushed to remote
bool isPushed = await cliRepo.IsCommitPushedAsync("commitHash", "origin/main");

// Get insertion/deletion diff statistics for a commit
var stat = await cliRepo.GetCommitStatAsync("HEAD");
Console.WriteLine($"Files changed: {stat.FilesChanged}, +{stat.Insertions}, -{stat.Deletions}");

// Get unified diff patch text
string diff = await cliRepo.GetDiffAsync("HEAD~1", "HEAD");
Console.WriteLine(diff);

// Read and write Git configuration entries
await cliRepo.SetConfigAsync("user.name", "Jane Doe");
string? userName = await cliRepo.GetConfigAsync("user.name");
```

---

## Features & APIs

### Remote Tracking & Push Inspection
- `GetTrackingStatusAsync(branch)`: Returns a `GitTrackingStatus` record detailing whether an upstream branch is configured, and computes ahead and behind commit counts using `git rev-list --left-right --count`.
  - Properties: `LocalBranch`, `UpstreamBranch`, `AheadCount`, `BehindCount`, `HasUpstream`, `HasUnpushedCommits`, `HasUnpulledCommits`, `IsSynchronized`.
  - Validates that the requested branch exists, throwing `ArgumentException` if a non-existent branch is passed.
- `IsCommitPushedAsync(commitHash, remoteBranch)`: Determines if a given commit is reachable from a specific remote tracking branch or any remote ref.

### Diff Inspection & Statistics
- `GetCommitStatAsync(commitIsh)`: Returns a `GitDiffStat` record (`FilesChanged`, `Insertions`, `Deletions`) parsed from `git show --shortstat`.
- `GetCommitDiffAsync(commitIsh, path)`: Returns the unified diff patch introduced by a specific commit, optionally scoped to a file path.
- `GetDiffAsync(fromCommit, toCommit, path)`: Returns unified diff output between two commits, a commit and the working tree, or unstaged working tree changes.

### Working Tree File Restoration & Revert
- `RestoreFileAsync(relativeFilePath, sourceCommit)`: Restores a working tree file to its state in `HEAD` (or an explicit `sourceCommit`), discarding working tree changes and ignoring uncommitted index changes.
- `RevertAsync(commitIsh, noCommit)`: Reverts the changes introduced by a commit (`git revert`), creating a revert commit or applying changes to the working tree and index when `noCommit` is set.

### Git Configuration Management
- `GetConfigAsync(key)`: Retrieves the effective configuration value for a key (e.g. `user.name`, `remote.origin.url`).
- `SetConfigAsync(key, value)`: Sets a local configuration key.
- `UnsetConfigAsync(key)`: Removes a configuration key.

### Remote Operations
- `FetchAsync(remote, branch, prune)`: Downloads objects and references from a remote without integrating them into the local branch.
- `PullAsync(remote, branch, rebase)`: Fetches and merges (or rebases) changes from a remote, returning a `GitMergeResult`.
- `PushAsync(remote, branch, force, setUpstream)`: Pushes local commits to a remote. When `force` is true, uses `--force-with-lease` to prevent overwriting unseen remote changes.

### Branch Management
- `GetCurrentBranchAsync()`: Returns the name of the currently checked out branch, or `"HEAD"` when the repository is in a detached HEAD state.
- `GetBranchesAsync(includeRemote)`: Lists local branches and optionally remote-tracking branches.
- `CreateBranchAsync(branchName, startPoint)`: Creates a new branch without switching to it.
- `CheckoutAsync(branchName, createNew, startPoint, updateWorkingTree)`: Switches branches. When `updateWorkingTree` is `false`, moves `HEAD` via `git symbolic-ref` without modifying disk files.
- `DeleteBranchAsync(branchName, force)`: Deletes a local branch.
- `RenameBranchAsync(oldName, newName)`: Renames a branch.

### Merging & Conflict Handling
- `MergeAsync(branch)`: Merges a branch into the current one and returns a `GitMergeResult` without throwing when merge conflicts occur.
- `IsMergeInProgressAsync()`: Returns `true` if the repository is currently in the middle of an unresolved merge.
- `GetConflictedFilesAsync()`: Lists the relative paths of files currently in conflict.
- `ResolveConflictAsync(relativeFilePath)`: Stages a resolved file (`git add`).
- `ContinueMergeAsync(commitMessage)`: Concludes the merge once all conflicts are resolved.
- `AbortMergeAsync()`: Aborts an in-progress merge and restores the pre-merge state (`git merge --abort`).

### Commit Operations
- `CommitAsync(message, metadata)`: Creates a commit via the CLI.
- `CommitAmendAsync(message, all)`: Amends the tip commit via the CLI.

---

## Automatic Synchronization with `GitRepositorySynchronizer`

`GitRepositorySynchronizer` builds on top of `GitCliRepository` to keep a local repository continuously synchronized with a remote:

- **Local-to-remote** synchronization is debounced: when the local repository changes (e.g. commits made via `Pmad.Git.LocalRepositories` or `Pmad.Git.HttpServer`), a push is scheduled after `GitSyncOptions.PushDebounceDelay` (5 minutes by default). Subsequent changes reset the delay, so a burst of local commits yields a single push.
- **Remote-to-local** synchronization runs periodically every `GitSyncOptions.PullInterval` (1 hour by default), and can be triggered on demand (e.g. from a webhook endpoint) via `TriggerRemoteSyncAsync()`.
- **Conflict detection & state machine**: When a pull encounters merge conflicts, the synchronizer transitions to `GitSyncState.Conflict`, exposes the list of conflicted files, and pauses automatic synchronization until conflicts are resolved or aborted.

### Setup

```csharp
using Pmad.Git.LocalRepositories;
using Pmad.Git.Cli;

var repository = GitRepository.Open("/path/to/repo");

// Creates the synchronizer and begins background monitoring
await using var synchronizer = repository.CreateSynchronizer(new GitSyncOptions
{
    Remote = "origin",
    Branch = "main",
    PushDebounceDelay = TimeSpan.FromMinutes(5),
    PullInterval = TimeSpan.FromHours(1),
});

// Trigger a manual pull/fetch at any time (e.g. webhook receiver)
await synchronizer.TriggerRemoteSyncAsync();
```

### Resolving Conflicts

```csharp
if (synchronizer.State == GitSyncState.Conflict)
{
    foreach (var file in synchronizer.Conflict!.ConflictedFiles)
    {
        // Fix conflict in file, then stage:
        await synchronizer.ResolveConflictAsync(file);
    }

    // Finish merge and resume automatic synchronization:
    await synchronizer.CompleteConflictResolutionAsync();

    // Or discard the merge and revert to the pre-merge state:
    // await synchronizer.AbortConflictResolutionAsync();
}
```

---

## Using Alongside `Pmad.Git.LocalRepositories`

`GitCliRepository` and `GitRepository` (or `GitRepositoryWithIndexAndWorkspace`) can safely cooperate on the same repository within the same process when sharing lock managers:

```csharp
using Pmad.Git.LocalRepositories;
using Pmad.Git.Cli;

var repository = GitRepository.Open("/path/to/repo");

// Pass repository to share its lock manager and automatic cache invalidation
var cliRepository = new GitCliRepository(repository);

await cliRepository.PullAsync();

// repository's caches are automatically refreshed
var head = await repository.GetCommitAsync();
```

When sharing a lock manager, all mutating operations performed by `GitCliRepository` (including `fetch`, `pull`, `push`, `checkout`, branch operations, `commit`, `amend`, merge resolution, `revert`, `restore`, and local `config` updates) acquire the shared in-process write lock used by `GitRepository`, coordinating operations and triggering automatic cache invalidation between cooperating instances in the same process. Note that this in-process lock only synchronizes cooperating instances within the current process; it does not protect against independent `git` CLI processes or external applications modifying the repository concurrently.
