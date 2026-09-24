# Pmad.Git

`Pmad.Git` is a suite of lightweight, modular .NET 8 libraries for inspecting, authoring, synchronizing, and serving Git repositories.

It is split into five focused packages:

| Package | Description | Core Dependencies |
|:---|:---|:---|
| **[Pmad.Git.LocalRepositories](src/Pmad.Git.LocalRepositories/README.md)** | 100% managed C# Git engine for local repository inspection, index (DIRC v2) reading/writing, working tree staging, workspace commit management, and synchronization abstractions (`IGitRepositoryWithRemote`, `GitRepositorySynchronizer`). Zero dependency on the `git` CLI or C-bindings. | Pure .NET 8 |
| **[Pmad.Git.RemoteClient](src/Pmad.Git.RemoteClient/README.md)** | 100% managed C# Smart HTTP client implementing `IGitRepositoryWithRemote` for clone, fetch, pull, push, and automated background synchronization without any CLI dependency. | Pure .NET 8 |
| **[Pmad.Git.Protocol](src/Pmad.Git.Protocol/README.md)** | Low-level Git wire protocol (pkt-line, capability negotiation) and streaming packfile reader/writer engine. | Pure .NET 8 |
| **[Pmad.Git.Cli](src/Pmad.Git.Cli/README.md)** | High-level Git CLI wrapper for remote synchronization (push/pull/fetch), branch management, tracking status, diff stats, and merge conflict resolution using the local `git` executable. | `git` executable |
| **[Pmad.Git.HttpServer](src/Pmad.Git.HttpServer/README.md)** | ASP.NET Core middleware enabling repositories to be served over the standard Git Smart HTTP protocol (`git-upload-pack` and `git-receive-pack`) with synchronizer caching service. | ASP.NET Core |

---

## Highlights

### 100% Pure Managed C# Core (`Pmad.Git.LocalRepositories`)
- **Zero CLI requirement**: Directly reads and writes the Git object database and index on disk without spawning processes or requiring native Git binaries.
- **Cross-platform**: Runs anywhere .NET 8 runs, including mobile operating systems (iOS/iPadOS, Android) and minimal container environments where `git.exe` is absent.
- **Binary Git Index Engine (`DIRC` v2)**: Complete, canonical implementation supporting stat cache fields (`ctime`, `mtime`, `dev`, `ino`, `fileMode`, `uid`, `gid`, `fileSize`), stage bits, and trailing SHA-1/SHA-256 checksums.
- **Working Tree & Staging**: Fast status scanning (`GetStatusAsync`) with stat-cache short-circuiting, comprehensive `.gitignore` rule matching, and staging primitives (`StageAsync`, `UnstageAsync`, `RestoreFileAsync`).
- **Workspace Commits & Resets**: Create commits from the index (`CommitAsync`), amend commits (`CommitAmendAsync`), perform `Soft`, `Mixed`, or `Hard` resets (`ResetAsync`), linear squash ranges (`SquashRangeAsync`), and commit reverts (`RevertAsync`).
- **SHA-1 and SHA-256 Support**: Full support for both standard 160-bit SHA-1 and modern 256-bit SHA-256 repositories.
- **Synchronization State Machine**: `GitRepositorySynchronizer` coordinates debounced push-on-change with periodic pull, exposing a state machine to safely detect, pause, and cooperatively resolve merge conflicts.

### Pure Managed Remote Client (`Pmad.Git.RemoteClient`)
- **Managed Smart HTTP Client**: Clone, fetch, pull, and push against remote repositories (GitHub, Azure DevOps, GitLab, Pmad.Git.HttpServer) without native Git or LibGit2Sharp.
- **Continuous Background Sync**: Wire up any local repository to its remote counterpart via `repository.CreateSynchronizer(new GitRemoteClientSyncOptions { ... })`.
- **Rich Authentication**: Support for Personal Access Tokens (PAT), HTTP Basic auth, Bearer tokens, and custom authorization headers.
- **In-Memory Three-Way Merges**: Full support for fast-forward and 3-way merges with conflict detection during pull.

### Git CLI Wrapper (`Pmad.Git.Cli`)
- **CLI-Backed Synchronization**: Alternative synchronizer implementation that offloads push/pull/fetch to the native `git` executable when present on the system.
- **Upstream Tracking Status**: Query configured upstream branches and calculate ahead/behind commit counts via `GetTrackingStatusAsync()`.
- **Diff Statistics & Patches**: Retrieve unified diff patches and parsed `+insertions, -deletions` diff statistics via `GetCommitStatAsync()` and `GetDiffAsync()`.

### Smart HTTP Server (`Pmad.Git.HttpServer`)
- Full implementation of the Git Smart HTTP protocol for hosting repositories in ASP.NET Core applications.
- Supports streaming pack generation and pack unpacking with memory-efficient pack readers and object walkers.
- Generalized `IGitRepositorySynchronizerService` supporting both pure managed (`Pmad.Git.RemoteClient`) and CLI-based (`Pmad.Git.Cli`) repository synchronization with automatic initial clone (`SetupSynchronizerAsync`).

---

## Getting Started

### Local Repository Inspection and Commits (Zero CLI)

```csharp
using Pmad.Git.LocalRepositories;

// Open an existing repository with index and workspace management (or use GitRepositoryWithIndexAndWorkspace.Init to create a new one)
using var repo = GitRepositoryWithIndexAndWorkspace.Open("/path/to/repo");

// Inspect status
var status = await repo.GetStatusAsync();

// Stage and commit changes
await repo.StageAsync("src/App.cs");
var commitHash = await repo.CommitAsync("Updated application entry point");

// Reset or amend if needed
await repo.CommitAmendAsync("Amended: Updated application entry point with tests");
```

### Pure Managed Remote Synchronization (Zero CLI)

```csharp
using Pmad.Git.LocalRepositories;
using Pmad.Git.RemoteClient;

// Open local repository
using var repo = GitRepositoryWithIndexAndWorkspace.Open("/path/to/repo");

// Create automated background synchronizer
await using var synchronizer = repo.CreateSynchronizer(new GitRemoteClientSyncOptions
{
    Url = "https://github.com/owner/repo.git",
    Credentials = GitHttpCredentials.PersonalAccessToken("your-token"),
    PushDebounceDelay = TimeSpan.FromSeconds(30),
    PullInterval = TimeSpan.FromMinutes(5)
});

// Any local commit automatically triggers a debounced push to the remote server!
await repo.StageAsync("README.md");
await repo.CommitAsync("Updated documentation");
```

### Remote Tracking and Synchronization via CLI

```csharp
using Pmad.Git.Cli;
using Pmad.Git.LocalRepositories;

// Open an existing repository and wrap with GitCliRepository
using var repo = GitRepository.Open("/path/to/repo");
var cliRepo = new GitCliRepository(repo);

// Check ahead/behind tracking status
var tracking = await cliRepo.GetTrackingStatusAsync();
if (tracking.HasUnpushedCommits)
{
    Console.WriteLine($"Ahead by {tracking.AheadCount} commits, pushing...");
    await cliRepo.PushAsync();
}
```

---

## Testing & Quality

All components are rigorously tested with over 1,400 automated tests covering unit logic, binary compatibility, end-to-end multi-client synchronization, and native `git` CLI validation (`git fsck --full --strict`):

```bash
dotnet test
```

---

## License

MIT Licensed and open source.

## Related Projects

- [Pmad.Wiki](https://github.com/jetelain/PmadWiki/): ASP.NET Core markdown wiki hosted on top of Git.