# Pmad.Git.RemoteClient

`Pmad.Git.RemoteClient` is a 100% pure managed C# Smart HTTP Git client library for .NET 8. It implements `IGitRepositoryWithRemote` from `Pmad.Git.LocalRepositories`, enabling complete remote Git operations—clone, fetch, pull, and push—without any dependency on native `git` binaries, CLI processes, or C-based libraries (like LibGit2Sharp).

It seamlessly pairs with `GitRepositorySynchronizer` to provide automated, debounced, background synchronization between local workspaces and remote Git servers.

---

## Highlights

- **Zero CLI or Native Dependencies**: Pure managed .NET 8 code that runs anywhere .NET runs, including containers, Azure Functions, AWS Lambda, macOS, Linux, Windows, and mobile operating systems (iOS, Android).
- **Git Smart HTTP Protocol**: Full support for reference discovery, pack negotiation (`upload-pack`), and push pack generation (`receive-pack`).
- **Workspace Integration**: Operates directly on local repositories (`Pmad.Git.LocalRepositories`), updating working trees, `.git/index` (DIRC v2), and reference stores.
- **Automated Background Synchronization**: Create a `GitRepositorySynchronizer` directly via `repository.CreateSynchronizer(options)` to enable debounced push-on-change and periodic remote pulling.
- **Merge & Conflict Resolution**: Handles fast-forward merges, three-way merges, conflict detection, and cooperative conflict resolution.
- **Flexible Authentication**: Built-in support for Basic, Personal Access Token (PAT), Bearer, and custom header credentials.
- **AOT & Trimming Compatible**: Pre-configured with `<IsAotCompatible>true</IsAotCompatible>` for Native AOT and high-performance trimmed deployments.

---

## Installation

Add a package reference to `Pmad.Git.RemoteClient`:

```bash
dotnet add package Pmad.Git.RemoteClient
```

---

## Authentication

Authentication is configured via `GitHttpCredentials` on `GitRemoteClientOptions` or `GitRemoteClientSyncOptions`:

```csharp
using Pmad.Git.RemoteClient;

// 1. Personal Access Token (GitHub, Azure DevOps, GitLab)
var patCredentials = GitHttpCredentials.PersonalAccessToken("your-github-pat");

// 2. HTTP Basic Authentication
var basicCredentials = GitHttpCredentials.Basic("username", "password");

// 3. Bearer Token (OAuth2 / OIDC)
var bearerCredentials = GitHttpCredentials.Bearer("access-token");

// 4. Custom HTTP Header
var customCredentials = GitHttpCredentials.Custom("X-Custom-Auth", "secret-value");
```

---

## Code Examples

### 1. Cloning a Remote Repository

```csharp
using Pmad.Git.RemoteClient;

var options = new GitRemoteClientOptions
{
    Credentials = GitHttpCredentials.PersonalAccessToken("ghp_yourTokenHere")
};

using var repo = await GitRemoteClientRepository.CloneAsync(
    "https://github.com/owner/repository.git",
    targetPath: @"C:\repos\my-repo",
    options: options);

Console.WriteLine($"Cloned branch {repo.WorkspaceRepository?.CurrentBranchName} to {repo.RootPath}");
```

### 2. Fetch, Pull, and Push on an Existing Local Repository

```csharp
using Pmad.Git.LocalRepositories;
using Pmad.Git.RemoteClient;

// Open an existing local workspace repository
using var localRepo = GitRepositoryWithIndexAndWorkspace.Open(@"C:\repos\my-repo");

// Wrap with managed remote client
var options = new GitRemoteClientOptions
{
    Credentials = GitHttpCredentials.PersonalAccessToken("ghp_yourTokenHere")
};
using var remoteRepo = new GitRemoteClientRepository(localRepo, options: options);

// Pull changes from remote origin (fetches and merges into local working tree)
var pullResult = await remoteRepo.PullAsync();
if (pullResult.HasConflicts)
{
    Console.WriteLine($"Conflicts encountered in: {string.Join(", ", pullResult.ConflictedFiles)}");
}
else
{
    Console.WriteLine("Pull completed successfully.");
}

// Make local changes, stage, and commit
File.WriteAllText(Path.Combine(localRepo.RootPath, "notes.txt"), "Updated notes.");
await localRepo.StageAsync("notes.txt");
await localRepo.CommitAsync("Update notes");

// Push commit to remote
await remoteRepo.PushAsync();
Console.WriteLine("Push completed.");
```

### 3. Automated Continuous Synchronization (`CreateSynchronizer`)

`Pmad.Git.RemoteClient` extends `IGitRepository` with `CreateSynchronizer`, linking local commits to the background `GitRepositorySynchronizer`:

```csharp
using Pmad.Git.LocalRepositories;
using Pmad.Git.RemoteClient;

using var localRepo = GitRepositoryWithIndexAndWorkspace.Open(@"C:\repos\my-repo");

var syncOptions = new GitRemoteClientSyncOptions
{
    Url = "https://github.com/owner/repository.git",
    Credentials = GitHttpCredentials.PersonalAccessToken("ghp_yourTokenHere"),
    Remote = "origin",
    Branch = "main",
    PushDebounceDelay = TimeSpan.FromSeconds(30), // Wait 30s of inactivity after commit before pushing
    PullInterval = TimeSpan.FromMinutes(5)        // Pull from remote every 5 minutes
};

// Create and automatically start background synchronizer
await using var synchronizer = localRepo.CreateSynchronizer(syncOptions);

// Local commit triggers debounced push automatically
await localRepo.StageAsync("data.json");
await localRepo.CommitAsync("Save state");

// Trigger an immediate pull on-demand (e.g. upon receiving a server webhook notification)
await synchronizer.TriggerRemoteSyncAsync();

// Or flush pending local changes immediately
await synchronizer.FlushPendingPushAsync();
```

### 4. Handling Conflicts in Synchronizer

```csharp
if (synchronizer.State == GitSyncState.Conflict)
{
    Console.WriteLine($"Conflicts pending in: {string.Join(", ", synchronizer.Conflict!.ConflictedFiles)}");

    // Resolve conflicted file in working tree
    var conflictedFile = synchronizer.Conflict.ConflictedFiles[0];
    File.WriteAllText(Path.Combine(localRepo.RootPath, conflictedFile), "resolved content");

    // Mark as resolved
    await synchronizer.ResolveConflictAsync(conflictedFile);

    // Finalize merge commit and resume automatic synchronization
    await synchronizer.CompleteConflictResolutionAsync("Resolved conflict");
}
```

---

## Comparison: `Pmad.Git.RemoteClient` vs `Pmad.Git.Cli`

| Feature | `Pmad.Git.RemoteClient` | `Pmad.Git.Cli` |
|:---|:---|:---|
| **Underlying Engine** | 100% Managed C# Smart HTTP Client | System `git` CLI executable |
| **External Dependencies** | None (pure .NET 8) | Requires `git` installed on host |
| **Platform Portability** | Universal (.NET 8, iOS, Android, minimal containers) | Desktop & Server with native Git |
| **Supported Protocols** | HTTP / HTTPS (Smart HTTP) | HTTP, HTTPS, SSH, Local file paths |
| **Background Synchronizer** | Supported (`GitRemoteClientSyncOptions`) | Supported (`GitCliSyncOptions`) |
| **Ahead/Behind Tracking** | Supported via local ref stores | Supported via `git rev-list` |
| **Native AOT Compatible** | Yes (`<IsAotCompatible>true</IsAotCompatible>`) | Yes |

---

## License

MIT
