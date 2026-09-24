# Pmad.Git.LocalRepositories

`Pmad.Git.LocalRepositories` is a lightweight .NET 8 library that lets you inspect and author local Git repositories completely in managed C#, without shelling out to the `git` executable. It can open repositories, resolve commits, enumerate trees, read blobs, stage files, read/write binary Git indexes, and execute workspace operations directly against the `.git` directory. Both SHA-1 and SHA-256 object formats are supported.

Because it has zero dependency on the native `git` CLI or C-bindings, it runs seamlessly across all platforms supported by .NET, including desktop operating systems, containers, and mobile platforms (iOS/Android) where `git.exe` is absent.

## Installation

Add a project reference to `Pmad.Git.LocalRepositories` or publish it as a package and reference it like any other NuGet dependency. The library targets .NET 8 and uses C# 12 features.

## Quick Start

### Basic Inspection and Object Store Commits

```csharp
using System.Text;
using Pmad.Git.LocalRepositories;

// Open an existing repository (or use GitRepository.Init to create a new one)
using var repository = GitRepository.Open("/path/to/repo");

var head = await repository.GetCommitAsync();
Console.WriteLine($"HEAD: {head.Id} -> {head.Message}");

await foreach (var item in repository.EnumerateCommitTreeAsync(path: "src"))
{
    Console.WriteLine($"{item.Path} ({item.Entry.Kind})");
}

var fileContent = await repository.ReadFileAsync("src/Program.cs");
Console.WriteLine(Encoding.UTF8.GetString(fileContent));

// Create a commit directly in the object store without touching working tree files
var metadata = new GitCommitMetadata(
    message: "Automated change",
    author: new GitCommitSignature("CI Bot", "ci@example.com", DateTimeOffset.UtcNow));

var commitId = await repository.CreateCommitAsync(
    branchName: "main",
    operations: [
        new AddFileOperation("src/NewFile.txt", Encoding.UTF8.GetBytes("payload"))
    ],
    metadata);

Console.WriteLine($"Created commit {commitId.Value}");
```

### Managed Workspace Repository (Working Tree & Index)

When you want Git working tree operations that synchronize with disk files and `.git/index` entirely in C#:

```csharp
using Pmad.Git.LocalRepositories;

// Open an existing repository with index and workspace support
using var workspaceRepo = GitRepositoryWithIndexAndWorkspace.Open("/path/to/repo");

// Check working tree status
var status = await workspaceRepo.GetStatusAsync();
foreach (var entry in status.Entries)
{
    Console.WriteLine($"{entry.Path}: {entry.WorkingTreeStatus}, Staged: {entry.IsStaged}");
}

// Capture baseline commit before making changes
var baseCommit = await workspaceRepo.GetCommitAsync();
var baseCommitHash = baseCommit.Id;

// Stage and commit working tree changes
await workspaceRepo.StageAsync("src/NewFile.txt");
var commitHash = await workspaceRepo.CommitAsync("Added new file");

// Amend the tip commit with additional staged changes
await workspaceRepo.StageAsync("README.md");
var commitToRevertHash = await workspaceRepo.CommitAmendAsync("Added new file and updated README");

// Revert a previous commit against the working tree and index
await workspaceRepo.RevertAsync(commitToRevertHash);

// Reset workspace (Soft, Mixed, or Hard) back to the base commit
await workspaceRepo.ResetAsync(baseCommitHash, GitResetMode.Hard);

// Squash a range of commits on the current branch (e.g. from baseCommitHash to HEAD)
// await workspaceRepo.SquashRangeAsync(baseCommitHash, "Milestone: feature complete");
```

---

## Features & APIs

### Creating and Opening Repositories
- `GitRepository.Init(path, bare, initialBranch)` initializes a new Git repository.
- `GitRepository.Open(path)` opens an existing repository (accepting either the working root or `.git` directory).
- `GitRepositoryWithIndexAndWorkspace.Init(path, initialBranch)` initializes a non-bare repository ready for workspace operations.
- `GitRepositoryWithIndexAndWorkspace.Open(path)` opens a repository with working tree and index management enabled.
- `GitRepository.LockManager` exposes an `IGitRepositoryLockManager` for in-process thread synchronization across reference and object operations.

### Managed Workspace Repository (`IGitWorkspaceRepository`)
`GitRepositoryWithIndexAndWorkspace` combines the low-level object store with working tree and `.git/index` management:
- `CommitAsync(message, metadata, stageAll)` writes a Git tree from the current index, creates a commit object, advances the active branch (or detached HEAD), and updates the index stat cache.
- `CommitAmendAsync(message, metadata, stageAll)` rewrites the tip commit with the current staged changes, preserving or updating commit metadata.
- `ResetAsync(commitHash, mode)` supports all three Git reset workflows:
  - `GitResetMode.Soft`: Moves HEAD to the target commit without modifying index or working tree.
  - `GitResetMode.Mixed`: Moves HEAD and resets the index to match the target commit tree.
  - `GitResetMode.Hard`: Moves HEAD, resets the index, restores file contents and permissions on disk, and deletes obsolete files.
- `SquashRangeAsync(baseCommitHash, message, metadata)` squashes a linear range of commits (`baseCommit..HEAD`) into a single milestone commit, validating ancestor reachability and updating workspace files.
- `RevertAsync(commitHash, metadata)` inverts the changes introduced by a commit directly against the index and working tree, ensuring the workspace is clean before executing.
- `IsWorkingTreeCleanAsync()` verifies whether there are any unstaged or staged modifications in the workspace.

### Working Tree Staging Engine (`GitIndexManager`)
`GitIndexManager` manages status scanning, staging, and `.git/index` updates:
- `GetStatusAsync()` scans the working tree and compares against index entries and HEAD. Uses a fast stat-cache comparison (`mtime`, `ctime`, file length, executable mode), falling back to blob hashing only when stat metadata differs.
- `StageAsync(path)` / `StageAllAsync()` stages file additions, modifications, and deletions into the index.
- `UnstageAsync(path)` / `UnstageAllAsync()` restores index entries from HEAD while preserving working-tree changes.
- `RestoreFileAsync(path)` / `RestoreAllAsync()` discards working-tree changes by restoring files from the index.
- Full `.gitignore` and `.git/info/exclude` rule evaluation via `GitIgnoreMatcher` (supports wildcards, leading/trailing slashes, directory anchors, and negation rules `!`).

### 100% Managed Binary Git Index (`DIRC` v2)
`GitIndex` and `GitIndexEntry` provide a complete pure-C# implementation of the canonical Git binary index format:
- Supports 10 stat-cache fields: `ctime`, `mtime`, `dev`, `ino`, `fileMode` (100644 vs 100755), `uid`, `gid`, `fileSize`, `hash`, and flags.
- Validates entry hash lengths against SHA-1 (20-byte) or SHA-256 (32-byte) index checksum formats.
- Enforces cross-process atomic file locking (`.git/index.lock`) with safe ownership cleanup.

### In-Process Tree Comparison & Commit Changes
- `CompareTreesAsync(oldTreeHash, newTreeHash)` compares two Git trees in-memory, returning a list of `GitTreeChange` records (`Path`, `Kind`, `OldHash`, `NewHash`) covering `Added`, `Modified`, and `Deleted` entries.
- `GetCommitChangesAsync(commitHash)` returns all file changes introduced by a commit relative to its first parent (or against an empty tree for root commits).

### Reference & Branch Management (`ReferenceStore`)
- `GetCurrentBranchNameAsync()` resolves the current branch name (e.g. `main`), properly handling symbolic references.
- `IsHeadDetachedAsync()` checks if `HEAD` points directly to an object ID rather than a symbolic branch reference.
- `CreateReferenceAsync(name, hash, overwrite)` creates or updates references (including safety backups under `refs/backups/*`, tags, or custom refs).
- `GetReferencesByPrefixAsync(prefix)` retrieves all references under a given prefix (e.g. `refs/heads/`, `refs/backups/`).
- `DeleteReferenceAsync(name)` safely deletes loose references and packed references.

### Commit Rewriting at Object Store Level
- `AmendCommitAsync(headHash, newTree, message, metadata)` creates an amended commit directly in the object database.
- `SquashCommitsAsync(baseCommit, targetCommit, message, metadata)` creates a linear squash commit object in the object database.
- `WriteTreeAsync(index)` writes tree objects from a `GitIndex` into the object store, rejecting unmerged (conflicted) entries.

### Cache Management & Reachability
- `InvalidateCaches(clearAllData)` clears cached references and loose-object metadata so subsequent operations reflect disk changes.
- `IsCommitReachableAsync(from, to)` traverses the commit graph to determine if `to` is reachable from `from`.

### Remote Synchronization Architecture (`IGitRepositoryWithRemote` & `GitRepositorySynchronizer`)
`Pmad.Git.LocalRepositories` defines the core interfaces and state machines for keeping local repositories synchronized with remotes:
- **`IGitRepositoryWithRemote`**: Standard contract defining remote communication and merge operations:
  - `FetchAsync`, `PullAsync`, `PushAsync`, `MergeAsync`.
  - Conflict queries and cooperative resolution: `IsMergeInProgressAsync`, `GetConflictedFilesAsync`, `ResolveConflictAsync`, `ContinueMergeAsync`, `AbortMergeAsync`.
  - Tracking queries: `GetTrackingStatusAsync`, `IsCommitPushedAsync`.
- **`GitRepositorySynchronizer`**: Coordinates continuous background synchronization:
  - Subscribes to local repository `Changed` events to debounce pushes (`PushDebounceDelay`).
  - Periodically pulls remote updates (`PullInterval`).
  - Implements a state machine (`Idle`, `Syncing`, `Conflict`).
  - In case of merge conflicts, halts automatic sync and exposes `Conflict` info, allowing safe resolution via `ResolveConflictAsync` and `CompleteConflictResolutionAsync`.
- **Pluggable Remote Implementations**:
  - **`Pmad.Git.RemoteClient`**: 100% managed C# Smart HTTP client without any CLI or native dependencies (`GitRemoteClientRepository`, `GitRemoteClientSyncOptions`).
  - **`Pmad.Git.Cli`**: Wrapper around system `git` CLI executable (`GitCliRepository`, `GitCliSyncOptions`).
- **Server-Side Integration (`Pmad.Git.HttpServer`)**:
  - `IGitRepositorySynchronizerService` manages, caches, and automatically disposes background synchronizers for server-hosted repositories, with factory overloads guaranteeing usage of canonical `IGitRepository` instances and `SetupSynchronizerAsync` for automated initial clone.

---

## Testing

The solution includes comprehensive unit tests and native `git` CLI interoperability tests in `tests/Pmad.Git.LocalRepositories.Test`:
- Verifies full binary round-trip compatibility between `GitIndex` and native `git.exe`.
- Validates repository integrity using `git fsck --full --strict` across managed commits, amends, squashes, and reverts.

Run tests via:
```bash
dotnet test tests/Pmad.Git.LocalRepositories.Test/Pmad.Git.LocalRepositories.Test.csproj
```
