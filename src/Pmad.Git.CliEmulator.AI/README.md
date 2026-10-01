# Pmad.Git.CliEmulator.AI

`Microsoft.Extensions.AI` integration for `Pmad.Git.CliEmulator`.

Exposes the managed Git CLI emulator as a single `git` **`AIFunction`** that any [`IChatClient`](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai)-based agent pipeline can call as a tool — no native `git` binary required.

## Features

- **One `git` tool, all sub-commands** — `status`, `log`, `diff`, `show`, `add`, `restore`, `commit`, `reset`, `revert`, `merge`, `branch`, `tag`, `config`, `remote`, `ls-tree`, `rev-parse`, `cat-file`, `fetch`, `pull`, `push`.
- **Array-args _or_ string-args** — pick the variant that best matches the model you're using.
- **Safe by default** — gated operations (destructive and network operations) are denied automatically unless you supply an `IUserApproval` implementation.
- **DI-friendly** — one-line `IServiceCollection` registration via `AddGitCliEmulatorAI`.
- **No native Git dependency** — pure managed C#, AOT-compatible via the underlying `Pmad.Git.CliEmulator`.

## Quick Start

### Option A — Extension method on the emulator

```csharp
using Pmad.Git.CliEmulator;
using Pmad.Git.CliEmulator.AI;
using Microsoft.Extensions.AI;

using var emulator = GitCliEmulator.Open("/path/to/repo");

AIFunction gitTool = emulator.CreateAIFunction();

IChatClient client = new ChatClientBuilder(innerClient)
    .UseFunctionInvocation()
    .Build();

var response = await client.CompleteAsync(
    "What files have changed since the last commit?",
    new ChatOptions { Tools = [gitTool] });
```

### Option B — Factory directly

```csharp
using Pmad.Git.CliEmulator;
using Pmad.Git.CliEmulator.AI;

using var emulator = GitCliEmulator.Open("/path/to/repo");

// Array-args variant (recommended — most models handle JSON arrays well)
AIFunction gitTool = GitAIFunctionFactory.Create(emulator);

// String-args variant (for models that prefer a single string)
AIFunction gitToolStr = GitAIFunctionFactory.CreateFromString(emulator);
```

### Option C — Dependency injection

```csharp
// Program.cs / Startup.cs
using Pmad.Git.CliEmulator.AI;
using Pmad.Git.RemoteClient;

builder.Services.AddGitCliEmulatorAI(
    repositoryPath: "/path/to/repo",
    remoteOptions: new GitRemoteClientOptions
    {
        Credentials = new GitHttpCredentials("pat", Environment.GetEnvironmentVariable("GH_TOKEN")!)
    });

// Inject AIFunction (or IEnumerable<AIFunction>) wherever you build your agent:
app.MapGet("/agent", async (AIFunction gitTool, IChatClient chat) =>
{
    var options = new ChatOptions { Tools = [gitTool] };
    var response = await chat
        .AsBuilder().UseFunctionInvocation().Build()
        .CompleteAsync("Summarise recent commits", options);
    return response.Message.Text;
});
```

## Approval Gates

By default, gated operations (destructive operations like `reset --hard` or network operations like `push`) are denied (`DenyApproval.Instance`) to protect repositories from unintended AI modifications. To permit gated operations or implement custom approval logic, provide an `IUserApproval` implementation (or `AutoApproval.Instance` to approve all operations) to any of the factory or extension methods:

```csharp
IUserApproval myApproval = new MyApproval(CancellationToken.None);

AIFunction gitTool = emulator.CreateAIFunction(myApproval);
// or
AIFunction gitTool = GitAIFunctionFactory.Create(emulator, myApproval);
```

## Package

| Package | NuGet |
|---|---|
| `Pmad.Git.CliEmulator.AI` | [![NuGet](https://img.shields.io/nuget/v/Pmad.Git.CliEmulator.AI)](https://www.nuget.org/packages/Pmad.Git.CliEmulator.AI) |

## Dependencies

| Package | Purpose |
|---|---|
| `Pmad.Git.CliEmulator` | Core Git CLI emulator |
| `Microsoft.Extensions.AI.Abstractions` | `AIFunction`, `AIFunctionFactory` |
| `Microsoft.Extensions.DependencyInjection.Abstractions` | `IServiceCollection` extensions |
