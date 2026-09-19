# Pmad.Git

`Pmad.Git` is a suite of lightweight, modular .NET 8 libraries for inspecting, authoring, synchronizing, and serving Git repositories.

It is split into three focused packages:

| Package | Description | Core Dependencies |
|:---|:---|:---|
| **[Pmad.Git.LocalRepositories](src/Pmad.Git.LocalRepositories/README.md)** | 100% managed C# Git engine for local repository inspection, index (DIRC v2) reading/writing, working tree staging, and workspace commit management. Zero dependency on the `git` CLI or C-bindings. | Pure .NET 8 |
| **[Pmad.Git.Cli](src/Pmad.Git.Cli/README.md)** | High-level Git CLI wrapper for remote synchronization (push/pull/fetch), branch management, tracking status, diff stats, and merge conflict resolution, including an automated background synchronizer. | `git` executable |
| **[Pmad.Git.HttpServer](src/Pmad.Git.HttpServer/README.md)** | ASP.NET Core middleware enabling repositories to be served over the standard Git Smart HTTP protocol (`git-upload-pack` and `git-receive-pack`). | ASP.NET Core |

---

## Highlights

### 100% Pure Managed C# Core (`Pmad.Git.LocalRepositories`)
- **Zero CLI requirement**: Directly reads and writes the Git object database and index on disk without spawning processes or requiring native Git binaries.
- **Cross-platform**: Runs anywhere .NET 8 runs, including mobile operating systems (iOS/iPadOS, Android) and minimal container environments where `git.exe` is absent.
- **Binary Git Index Engine (`DIRC` v2)**: Complete, canonical implementation supporting stat cache fields (`ctime`, `mtime`, `dev`, `ino`, `fileMode`, `uid`, `gid`, `fileSize`), stage bits, and trailing SHA-1/SHA-256 checksums.
- **Working Tree & Staging**: Fast status scanning (`GetStatusAsync`) with stat-cache short-circuiting, comprehensive `.gitignore` rule matching, and staging primitives (`StageAsync`, `UnstageAsync`, `RestoreFileAsync`).
- **Workspace Commits & Resets**: Create commits from the index (`CommitAsync`), amend commits (`CommitAmendAsync`), perform `Soft`, `Mixed`, or `Hard` resets (`ResetAsync`), linear squash ranges (`SquashRangeAsync`), and commit reverts (`RevertAsync`).
- **SHA-1 and SHA-256 Support**: Full support for both standard 160-bit SHA-1 and modern 256-bit SHA-256 repositories.

### Advanced Remote Synchronization & Polish (`Pmad.Git.Cli`)
- **Upstream Tracking Status**: Query configured upstream branches and calculate ahead/behind commit counts via `GetTrackingStatusAsync()`.
- **Remote Push Inspection**: Check if any local commit has been pushed to a remote branch via `IsCommitPushedAsync()`.
- **Diff Statistics & Patches**: Retrieve unified diff patches and parsed `+insertions, -deletions` diff statistics via `GetCommitStatAsync()` and `GetDiffAsync()`.
- **Automated Background Sync**: `GitRepositorySynchronizer` coordinates debounced push-on-change with periodic pull, exposing a state machine to safely pause and resolve merge conflicts.
- **Seamless Coexistence**: Automatically synchronizes in-process locks and cache invalidation when used alongside `Pmad.Git.LocalRepositories`.

### Smart HTTP Server (`Pmad.Git.HttpServer`)
- Full implementation of the Git Smart HTTP protocol for hosting repositories in ASP.NET Core applications.
- Supports streaming pack generation and pack unpacking with memory-efficient pack readers and object walkers.

---

## Getting Started

### Local Repository Inspection and Commits (Zero CLI)

```csharp
using Pmad.Git.LocalRepositories;

// Open repository with index and workspace management
using var repo = GitRepositoryWithIndexAndWorkspace.Open("/path/to/repo");

// Inspect status
var status = await repo.GetStatusAsync();

// Stage and commit changes
await repo.StageAsync("src/App.cs");
var commitHash = await repo.CommitAsync("Updated application entry point");

// Reset or amend if needed
await repo.CommitAmendAsync("Amended: Updated application entry point with tests");
```

### Remote Tracking and Synchronization (CLI)

```csharp
using Pmad.Git.Cli;
using Pmad.Git.LocalRepositories;

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

All components are rigorously tested with over 1,270 automated tests covering unit logic, binary compatibility, and end-to-end native `git` CLI validation (`git fsck --full --strict`):

```bash
dotnet test
```

---

## License

MIT Licensed and open source.

## Related Projects

- [Pmad.Wiki](https://github.com/jetelain/PmadWiki/): ASP.NET Core markdown wiki hosted on top of Git.