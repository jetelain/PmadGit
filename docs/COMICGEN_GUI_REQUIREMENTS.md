# Pmad.Git Requirements & Feature Gap Report for ComicGen GUI

**Document Date:** September 19, 2026  
**Target Projects:** `Pmad.Git.LocalRepositories` & `Pmad.Git.Cli`  
**Consumer Project:** ComicGen Graphic User Interface (`docs/DESIGN_GUI.md`)  
**Author:** Pair programming analysis for library author

---

## 1. Executive Summary & Context

ComicGen is expanding with a desktop and tablet graphical authoring environment (.NET 10 / MAUI Blazor Hybrid). The application architecture designates **Git as the workspace backbone**:
- **Atomic Commits:** Every user explicit save, background auto-save cycle, and embedded AI agent file mutation creates an atomic commit.
- **Auto-Save Amend Strategy:** Continuous drafting amends the previous draft commit (`git commit --amend --no-edit`) to cap micro-commits to at most 1 commit per editing session.
- **Milestone Squashing:** Before story publishing or upon reaching `READY` status, drafting micro-commits are squashed into clean milestone commits (`story(01): completed initial draft`) with automated safety backup refs (`refs/backups/*`).
- **Audit Trail & History View:** Bidirectional navigation between Git commits and AI agent conversation turns, commit logs with color-coded author badges (User vs. Agent), and commit diff summaries (`+4, -2`).
- **Safety Gates & Undo:** Prominent *"Undo Agent Step"* button (revert / rollback), pre-flight checks ensuring the working tree is clean before destructive actions, and safeguards preventing rewriting remote-tracked commits.
- **Background Synchronization:** In-process auto-fetch, debounced push-on-change, and conflict detection via `GitRepositorySynchronizer`.

### What `Pmad.Git` Already Does Well
- **Concurrency & Locking:** `GitRepositoryLockManager` provides the reader/writer lock model (`AcquireReferenceLockAsync` / `LockAllAsync`) needed to serialize user saves, agent mutations, and CLI subprocesses.
- **Background Sync:** `GitRepositorySynchronizer` already implements debounced push, periodic pull, cache invalidation, and merge conflict state machines.
- **Object Store Queries:** `Pmad.Git.LocalRepositories` provides fast, in-process commit enumeration (`EnumerateCommitsAsync`), blob streaming (`ReadFileStreamAsync`), path existence checks, reachability checks (`IsCommitReachableAsync`), and commit last-change caching (`GetFilesWithLastChangeAsync`).
- **Remote Primitives:** `GitCliRepository` wraps `CloneAsync`, `FetchAsync`, `PullAsync`, `PushAsync`, branch CRUD, and merge conflict resolution (`MergeAsync`, `ResolveConflictAsync`, `ContinueMergeAsync`, `AbortMergeAsync`).

---

## 2. Core Architectural Distinction: Object Store vs. Working Tree & Index

Before specifying individual APIs, a crucial architectural distinction must be noted:

### The `.git/index` Desynchronization Issue
- In ComicGen GUI, users and agents edit physical files **on disk in the working directory** (e.g. `stories/01/panels/001.md`).
- `Pmad.Git.LocalRepositories.CreateCommitAsync` operates directly on the Git object store: it receives in-memory byte arrays/streams, writes loose objects into `.git/objects`, and updates `refs/heads/<branch>`.
- **`CreateCommitAsync` does not read or update `.git/index` (the staging area)**.
- If an in-process commit advances `HEAD` while `.git/index` retains the previous commit's tree hashes and stat cache:
  - Standard Git CLI commands (`git status`, `git checkout`, `git diff`, `git pull`) will report the repository as dirty or report conflicting staged/unstaged changes because `.git/index` does not match `HEAD`.
- **Design Guidance for Pmad.Git:**
  1. **Working Tree Operations:** For repositories with a live working tree, commits, amending, staging, and status checks are most reliably driven through `Pmad.Git.Cli` (which keeps `.git/index` and the working tree in sync).
  2. **In-Process Object Store Operations:** `Pmad.Git.LocalRepositories` is best suited for high-performance, read-heavy queries (commit graphs, tree inspection, diffing, reading file versions, last-change detection) and bare/virtual object operations.
  3. **Index Refresh Helper:** If `CreateCommitAsync` is used on a working tree repository, `Pmad.Git` should provide an index-refresh mechanism (e.g., executing `git read-tree HEAD` or a managed index updater) to prevent index divergence.

---

## 3. Detailed Feature Specifications & Proposed APIs

### Feature 1: Commit Amending (`git commit --amend`)

#### ComicGen GUI Requirement
During drafting, ComicGen evaluates the active repository state:
1. If `HEAD` is an unpushed automatic drafting commit authored by the current user for the active entity, ComicGen amends it (`git commit --amend --no-edit`).
2. This ensures continuous auto-saving produces at most 1 draft commit per session without polluting Git history.

#### Gaps in Current Pmad.Git
- `LocalRepositories`: `CreateCommitAsync` always sets the commit parent to current `HEAD`. It does not support amending (setting parents to `HEAD.Parents` and replacing `HEAD`).
- `Cli`: `GitCliRepository` has no general `CommitAsync` or `CommitAmendAsync` wrapper.

#### Proposed APIs

##### In `Pmad.Git.Cli`:
```csharp
/// <summary>
/// Creates a new commit from currently staged changes (or all tracked changes when all is true).
/// </summary>
public async Task CommitAsync(
    string message, 
    bool all = false, 
    CancellationToken cancellationToken = default)
{
    var args = new List<string> { "commit", "-m", message };
    if (all) args.Add("-a");
    using var writeLock = await LockWriteAsync(cancellationToken).ConfigureAwait(false);
    var result = await RunGit(cancellationToken, args.ToArray());
    result.EnsureSuccess();
    InvalidateCaches();
}

/// <summary>
/// Amends the current HEAD commit.
/// </summary>
/// <param name="message">New commit message. If null and noEdit is true, reuses existing message.</param>
/// <param name="noEdit">When true and message is null, keeps the existing commit message.</param>
/// <param name="all">When true, stages all modified and deleted files before amending.</param>
public async Task CommitAmendAsync(
    string? message = null, 
    bool noEdit = true, 
    bool all = false, 
    CancellationToken cancellationToken = default)
{
    var args = new List<string> { "commit", "--amend" };
    if (noEdit && message == null)
    {
        args.Add("--no-edit");
    }
    else if (message != null)
    {
        args.Add("-m");
        args.Add(message);
    }
    if (all) args.Add("-a");

    using var writeLock = await LockWriteAsync(cancellationToken).ConfigureAwait(false);
    var result = await RunGit(cancellationToken, args.ToArray());
    result.EnsureSuccess();
    InvalidateCaches();
}
```

##### In `Pmad.Git.LocalRepositories` (for in-memory object store operations):
```csharp
/// <summary>
/// Amends the current HEAD commit on the specified branch by replacing it with a new commit
/// sharing the same parent(s) as the current HEAD.
/// </summary>
public async Task<GitHash> AmendCommitAsync(
    string branchName,
    IEnumerable<GitCommitOperation> operations,
    GitCommitMetadata? metadata = null, // null = preserve existing author/message, update timestamp
    CancellationToken cancellationToken = default);
```

---

### Feature 2: Commit Squashing & History Rewriting

#### ComicGen GUI Requirement
When a story is marked `READY` or prior to publishing, non-technical authors are guided through squashing drafting micro-commits into a single milestone commit (`story(01): completed initial draft`).

#### Gaps in Current Pmad.Git
Neither `LocalRepositories` nor `Cli` provides an API to squash a commit range or reset/rebase.

#### Proposed APIs

##### In `Pmad.Git.LocalRepositories` (In-process squash — fast and zero-subprocess):
Since `LocalRepositories` creates Git commit objects directly, squashing in-process is extremely clean:
1. Obtain the root tree hash of current `HEAD`.
2. Construct a new commit object whose tree is that tree, whose sole parent is `baseCommitHash` (the ancestor commit before the drafting session), and whose message is the milestone message.
3. Update the branch reference to point to the new commit.

```csharp
/// <summary>
/// Creates a single squashed commit containing the tree of current HEAD, with its parent
/// set to baseCommitHash, and advances the branch reference to the new commit.
/// </summary>
public async Task<GitHash> SquashCommitsAsync(
    string branchName,
    GitHash baseCommitHash,
    GitCommitMetadata metadata,
    CancellationToken cancellationToken = default);
```

##### In `Pmad.Git.Cli` (Working tree consistency):
```csharp
public enum GitResetMode { Soft, Mixed, Hard }

/// <summary>
/// Resets the current branch to a specific commit.
/// </summary>
public async Task ResetAsync(
    string commitIsh, 
    GitResetMode mode = GitResetMode.Mixed, 
    CancellationToken cancellationToken = default)
{
    var flag = mode switch
    {
        GitResetMode.Soft => "--soft",
        GitResetMode.Mixed => "--mixed",
        GitResetMode.Hard => "--hard",
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
    using var writeLock = await LockWriteAsync(cancellationToken).ConfigureAwait(false);
    var result = await RunGit(cancellationToken, "reset", flag, commitIsh);
    result.EnsureSuccess();
    InvalidateCaches();
}

/// <summary>
/// Squashes all commits from baseCommitIsh to HEAD into a single commit with the given message.
/// Uses soft reset to preserve working tree changes followed by commit.
/// </summary>
public async Task SquashRangeAsync(
    string baseCommitIsh, 
    string commitMessage, 
    CancellationToken cancellationToken = default)
{
    await ResetAsync(baseCommitIsh, GitResetMode.Soft, cancellationToken);
    await CommitAsync(commitMessage, cancellationToken: cancellationToken);
}
```

---

### Feature 3: Working Tree Status & Dirty Checks

#### ComicGen GUI Requirement
- Destructive operations (branch checkout, reset, revert, squash, push) require safety gates to verify uncommitted editor work is not lost.
- Squashing explicitly requires: *"the working tree must be clean"*.
- Welcome / Recents hub surfaces whether a repository has dirty uncommitted changes.

#### Gaps in Current Pmad.Git
- Neither library provides a status check. `GitCliRepository` only checks `IsMergeInProgressAsync` and `GetConflictedFilesAsync`.

#### Proposed APIs in `Pmad.Git.Cli`:
```csharp
public sealed record GitStatusEntry(string Path, char IndexStatus, char WorkTreeStatus);

public sealed record GitStatusResult(
    bool IsClean,
    IReadOnlyList<string> StagedFiles,
    IReadOnlyList<string> ModifiedFiles,
    IReadOnlyList<string> UntrackedFiles,
    IReadOnlyList<string> DeletedFiles,
    IReadOnlyList<GitStatusEntry> Entries);

/// <summary>
/// Inspects the working tree and index status using 'git status --porcelain=v1 -z'.
/// </summary>
public async Task<GitStatusResult> GetStatusAsync(CancellationToken cancellationToken = default);

/// <summary>
/// Quick check returning true if there are no modified, untracked, or staged changes.
/// </summary>
public async Task<bool> IsWorkingTreeCleanAsync(CancellationToken cancellationToken = default);
```

---

### Feature 4: Staging & Adding Files (`stage`)

#### ComicGen GUI Requirement
`DESIGN_GUI.md` lists `stage` among the expected capabilities. When editing files on disk, selective staging or staging all changes is required before committing.

#### Gaps in Current Pmad.Git
- `GitCliRepository` only has `ResolveConflictAsync(file)` (which executes `git add -- <file>`).
- It lacks general `StageAsync`, `StageAllAsync`, and `UnstageAsync`.

#### Proposed APIs in `Pmad.Git.Cli`:
```csharp
/// <summary>
/// Stages specific file paths (git add -- <paths>).
/// </summary>
public async Task StageAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default);

/// <summary>
/// Stages all changes in the working tree, including new and deleted files (git add -A).
/// </summary>
public async Task StageAllAsync(CancellationToken cancellationToken = default);

/// <summary>
/// Unstages specific file paths (git restore --staged -- <paths> or git reset HEAD -- <paths>).
/// </summary>
public async Task UnstageAsync(IEnumerable<string> relativePaths, CancellationToken cancellationToken = default);
```

---

### Feature 5: Tree & Commit Diffs (Summaries, Changed Files, Patches)

#### ComicGen GUI Requirement
- **History View:** Displays commit log with author badges and diff summaries (`+4, -2`).
- **Agent Copilot Stream:** Displays inline diff chips: `agent(edit): modified 003.md (+4, -2) [View Diff]`.
- **Diff Viewer:** Inspects differences between commits or working tree.

#### Gaps in Current Pmad.Git
- `LocalRepositories`: No tree comparison or diffing APIs (only `GetFilesWithLastChangeAsync` which returns the single last commit that touched a file).
- `Cli`: No diff methods (except conflict file inspection).

#### Proposed APIs

##### In `Pmad.Git.LocalRepositories` (In-process tree comparison):
`LocalRepositories` already has `LoadLeafEntriesAsync(treeHash)` which produces `Dictionary<string, TreeLeaf>`. Comparing two trees in memory to find added, modified, and deleted files is pure C# logic:

```csharp
public enum GitChangeKind { Added, Modified, Deleted }

public sealed record GitTreeChange(
    string Path, 
    GitChangeKind Kind, 
    GitHash? OldHash, 
    GitHash? NewHash);

/// <summary>
/// Compares two trees and returns the list of added, modified, and deleted files.
/// </summary>
public async Task<IReadOnlyList<GitTreeChange>> CompareTreesAsync(
    GitHash oldTreeHash, 
    GitHash newTreeHash, 
    CancellationToken cancellationToken = default);

/// <summary>
/// Returns the file-level changes introduced by a commit compared to its primary parent.
/// If the commit has no parent (root commit), all files in the tree are reported as Added.
/// </summary>
public async Task<IReadOnlyList<GitTreeChange>> GetCommitChangesAsync(
    GitHash commitHash, 
    CancellationToken cancellationToken = default);
```

##### In `Pmad.Git.Cli` (Line stats `+N, -M` and patch text):
```csharp
public sealed record GitDiffStat(int FilesChanged, int Insertions, int Deletions);

/// <summary>
/// Returns line insertion/deletion statistics for a commit (git show --shortstat).
/// </summary>
public async Task<GitDiffStat> GetCommitStatAsync(string commitIsh, CancellationToken cancellationToken = default);

/// <summary>
/// Returns the unified diff output for a commit or between two commits/working tree (git diff / git show).
/// </summary>
public async Task<string> GetDiffAsync(
    string? fromCommit = null, 
    string? toCommit = null, 
    string? path = null, 
    CancellationToken cancellationToken = default);
```

---

### Feature 6: Remote Tracking Status (Ahead / Behind / Unpushed Detection)

#### ComicGen GUI Requirement
- Auto-save amend policy requires knowing if `HEAD` has **not** been pushed to a remote tracking branch.
- Safe squashing prohibits rewriting remote-tracked commits without explicit user confirmation.

#### Gaps in Current Pmad.Git
- `LocalRepositories`: Has `IsCommitReachableAsync(from, to)`, but cannot identify the configured upstream tracking branch because `.git/config` is not parsed.
- `Cli`: Has `GetCurrentBranchAsync()` and `GetBranchesAsync()`, but no method to query the upstream tracking branch or count ahead/behind commits.

#### Proposed APIs in `Pmad.Git.Cli`:
```csharp
public sealed record GitTrackingStatus(
    string BranchName,
    string? UpstreamBranchName,
    int CommitsAhead,
    int CommitsBehind,
    bool HasUpstream => UpstreamBranchName != null);

/// <summary>
/// Gets the upstream tracking status (ahead/behind counts) for the specified branch (defaults to current branch).
/// Uses 'git rev-list --left-right --count <branch>...<upstream>'.
/// </summary>
public async Task<GitTrackingStatus> GetTrackingStatusAsync(
    string? branchName = null, 
    CancellationToken cancellationToken = default);

/// <summary>
/// Checks whether a commit has already been pushed to the remote tracking branch.
/// </summary>
public async Task<bool> IsCommitPushedAsync(
    string commitIsh, 
    string? remote = null, 
    CancellationToken cancellationToken = default);
```

---

### Feature 7: "Undo Agent Step", Rollback, and Revert

#### ComicGen GUI Requirement
- Menu bar button: *"Undo Agent Step"*.
- Safely rolls back the last agent action via `git revert` or `reset --hard` (gated by confirmation).
- File restoration: Discard dirty editor changes to a specific file on disk.

#### Gaps in Current Pmad.Git
Neither library provides `revert`, `reset`, or working-tree file restoration.

#### Proposed APIs in `Pmad.Git.Cli`:
```csharp
/// <summary>
/// Reverts a commit by creating a new commit recording inverted changes (git revert).
/// </summary>
public async Task RevertAsync(
    string commitIsh, 
    bool noCommit = false, 
    CancellationToken cancellationToken = default);

/// <summary>
/// Restores working tree files to their state in HEAD or a specified commit (git restore / checkout).
/// </summary>
public async Task RestoreFileAsync(
    string relativeFilePath, 
    string? sourceCommit = null, 
    CancellationToken cancellationToken = default);
```

---

### Feature 8: Safety Backup References (`refs/backups/*`)

#### ComicGen GUI Requirement
Before squashing or major rewrites, ComicGen creates an automated backup reference: `refs/backups/draft-<timestamp>`. The user profile allows configuring retention limits to prune older backups.

#### Gaps in Current Pmad.Git
- `LocalRepositories`: `IGitReferenceStore.WriteReferenceWithValidationAsync` can write any `refs/...` path, but `IGitRepository` lacks high-level convenience methods to create, list by prefix, and delete arbitrary references.
- `Cli`: Only manages branches under `refs/heads/`.

#### Proposed APIs in `Pmad.Git.LocalRepositories` (`IGitRepository`):
```csharp
/// <summary>
/// Creates or updates a reference in any namespace (e.g. refs/backups/draft-20260919).
/// </summary>
Task CreateReferenceAsync(
    string referencePath, 
    GitHash targetCommit, 
    bool overwrite = false, 
    CancellationToken cancellationToken = default);

/// <summary>
/// Lists all references matching a specific prefix (e.g. 'refs/backups/').
/// </summary>
Task<IReadOnlyDictionary<string, GitHash>> GetReferencesByPrefixAsync(
    string prefix, 
    CancellationToken cancellationToken = default);

/// <summary>
/// Deletes a reference in any namespace.
/// </summary>
Task DeleteReferenceAsync(
    string referencePath, 
    CancellationToken cancellationToken = default);
```

---

### Feature 9: Git Configuration & Author Identity

#### ComicGen GUI Requirement
Preferences window (`Ctrl+,`) displays and configures:
- Author Name & Author Email (`user.name`, `user.email`).
- Default remote push target and sync cadence.

#### Gaps in Current Pmad.Git
- `LocalRepositories`: Only contains a tiny private INI reader in `GitObjectStore.cs` for `extensions.objectformat`.
- `Cli`: No `git config` getters or setters.

#### Proposed APIs in `Pmad.Git.Cli`:
```csharp
/// <summary>
/// Reads a git configuration value (git config [--global] <key>). Returns null if unset.
/// </summary>
public async Task<string?> GetConfigAsync(
    string key, 
    bool global = false, 
    CancellationToken cancellationToken = default);

/// <summary>
/// Sets a git configuration value (git config [--global] <key> <value>).
/// </summary>
public async Task SetConfigAsync(
    string key, 
    string value, 
    bool global = false, 
    CancellationToken cancellationToken = default);
```

---

### Feature 10: In-Process Active Branch & HEAD Inspection

#### ComicGen GUI Requirement
Recent franchises hub and menu bar display the active Git branch name.

#### Gaps in Current Pmad.Git
- In `LocalRepositories`: `ResolveHeadAsync()` reads `.git/HEAD`, detects `ref: refs/heads/main`, but immediately resolves it to `GitHash` and discards the branch name. There is no way to query the active branch name through `IGitRepository`!
- In `GitCliRepository`: `GetCurrentBranchAsync()` shells out to `git rev-parse --abbrev-ref HEAD`.

#### Proposed API in `Pmad.Git.LocalRepositories` (`IGitRepository` / `IGitReferenceStore`):
```csharp
/// <summary>
/// Returns the name of the currently checked out branch (e.g. "main"), 
/// or null if HEAD is detached or points to an invalid ref.
/// Reads .git/HEAD in-process without spawning a subprocess.
/// </summary>
Task<string?> GetCurrentBranchNameAsync(CancellationToken cancellationToken = default);

/// <summary>
/// Checks whether HEAD is detached (points directly to a commit hash rather than a symbolic ref).
/// </summary>
Task<bool> IsHeadDetachedAsync(CancellationToken cancellationToken = default);
```

---

## 4. Summary Matrix & Implementation Priority

| Feature Area | Required Capability | LocalRepositories | Cli | Priority for ComicGen GUI |
|---|---|---|---|---|
| **Auto-Save** | Amend last commit (`commit --amend`) | ❌ Lacks amend parent handling | ❌ No `CommitAmendAsync` | **P0 (Critical)** |
| **Milestone Squash** | Squash commit range to single commit | ❌ Lacks custom parent squash | ❌ No squash/reset wrapper | **P0 (Critical)** |
| **Safety Gates** | Check if working tree is clean | ❌ No disk status | ❌ No `GetStatusAsync` | **P0 (Critical)** |
| **Safety Gates** | Check if commit is pushed to upstream | ⚠️ `IsCommitReachable` only | ❌ No ahead/behind / upstream | **P0 (Critical)** |
| **History View** | Commit diff summary (`+4, -2`) & changed files | ❌ No tree diff | ❌ No diff stat parser | **P1 (High)** |
| **Agent Copilot** | "Undo Agent Step" (Revert / Reset) | ❌ No revert/reset | ❌ No revert/reset | **P1 (High)** |
| **Staging Area** | Stage files before commit (`git add`) | ❌ No index concept | ⚠️ Conflict-only `add` | **P1 (High)** |
| **Preferences** | Read/write `user.name`, `user.email` | ❌ No config API | ❌ No config API | **P1 (High)** |
| **Safety Backups** | Create & prune `refs/backups/*` | ⚠️ Low-level store only | ❌ Branches only | **P2 (Medium)** |
| **Recent Hub** | In-process current branch name | ❌ `ResolveHead` drops name | ⚠️ Spawns CLI process | **P2 (Medium)** |
| **Agent Copilot** | Inline diff chip (`agent(edit): (+4, -2)`) | ❌ No diff | ❌ No diff method | **P2 (Medium)** |
| **Background Sync** | Auto-fetch, push on debounce, merge | ❌ N/A | ✅ `GitRepositorySynchronizer` | **Already Complete** |
| **Cloning** | Clone repo during onboarding | ❌ N/A | ✅ `GitCliRepository.CloneAsync` | **Already Complete** |

---

## 5. Recommended Implementation Plan for Pmad.Git

### Phase 1: High-Impact Additions in `Pmad.Git.Cli`
1. `GetStatusAsync()` & `IsWorkingTreeCleanAsync()` using `git status --porcelain=v1 -z`.
2. `CommitAsync()` & `CommitAmendAsync()`.
3. `StageAsync()`, `StageAllAsync()`, `UnstageAsync()`.
4. `GetTrackingStatusAsync()` & `IsCommitPushedAsync()` using `git rev-list`.
5. `ResetAsync()` & `SquashRangeAsync()`.
6. `RevertAsync()` & `RestoreFileAsync()`.
7. `GetConfigAsync()` & `SetConfigAsync()`.
8. `GetCommitStatAsync()` & `GetDiffAsync()`.

### Phase 2: High-Performance Additions in `Pmad.Git.LocalRepositories`
1. `GetCurrentBranchNameAsync()` & `IsHeadDetachedAsync()` on `IGitReferenceStore` (in-process `HEAD` read).
2. `CompareTreesAsync()` & `GetCommitChangesAsync()` (in-process tree diff returning added/modified/deleted files).
3. `AmendCommitAsync()` & `SquashCommitsAsync()` (in-process commit object creation).
4. `CreateReferenceAsync()`, `GetReferencesByPrefixAsync()`, `DeleteReferenceAsync()` for custom ref namespaces like `refs/backups/*`.

