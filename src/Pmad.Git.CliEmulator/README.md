# Pmad.Git.CliEmulator

Managed Git CLI emulator for AI agents on top of `Pmad.Git.LocalRepositories` and `Pmad.Git.RemoteClient`.

## Overview

`Pmad.Git.CliEmulator` gives embedded AI agents a familiar, standard Git CLI command surface (`status`, `log`, `diff`, `add`, `commit`, `branch`, `merge`, `reset`, etc.) without requiring a native `git` binary, while giving the host application deep control via an asynchronous `IUserApproval` callback.

## Features

- **No native Git dependency**: Pure managed C# implementation.
- **Native AOT compatible**: Fully compatible with `<PublishAot>true</PublishAot>`.
- **Approval gates**: Every destructive or network operation calls `IUserApproval` with rich context objects (affected branch, commit history to lose, overwritten files, remote URL, etc.).
- **Exit codes and stream separation**: Returns `GitCliResponse` with `ExitCode`, `StdOut`, and `StdErr`.
- **Exit code 130 on denial / cancellation**: Denying an approval returns exit code 130 with a descriptive error message.
- **No line-ending normalization**: `core.autocrlf` and line-ending conversions are out of scope; staged files are written bit-for-bit, assuming Unix line separators (`\n`).

## Usage

### Streamlined One-Liner (Full Managed Stack)

```csharp
using Pmad.Git.CliEmulator;
using Pmad.Git.RemoteClient;

// Automatically opens local workspace and sets up managed Smart HTTP remote client:
using var emulator = GitCliEmulator.Open(repositoryPath, new GitRemoteClientOptions
{
    Credentials = new GitHttpCredentials("pat", "my-token")
});

var response = await emulator.InvokeAsync(["status"], userApproval);
Console.WriteLine(response.StdOut);
```

### Fluent Extension Methods

```csharp
using Pmad.Git.CliEmulator;
using Pmad.Git.LocalRepositories;

using var repo = GitRepositoryWithIndexAndWorkspace.Open(repositoryPath);
var emulator = repo.CreateCliEmulator();
```
