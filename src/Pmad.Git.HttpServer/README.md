# Pmad.Git.HttpServer

`Pmad.Git.HttpServer` is an ASP.NET Core library that lets git synchronize with a server side stored local repository using the Git Smart HTTP protocol.

## Getting Started

1. Add a reference to `Pmad.Git.HttpServer` in your ASP.NET Core application.
2. Register the endpoints inside `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
});

var app = builder.Build();
app.MapGitSmartHttp();
app.Run();
```

With the sample above, git clients can clone/fetch repositories stored under `/srv/git` using the Smart HTTP endpoints (`/git/{repository}.git/info/refs`, `/git/{repository}.git/git-upload-pack`).

## Configuration

`GitSmartHttpOptions` controls behavior:

- `RepositoryRoot`: Absolute path containing repositories. Mandatory.
- `EnableUploadPack`: Allows `git-upload-pack` (fetch/clone). Enabled by default.
- `EnableReceivePack`: Allows `git-receive-pack` (push). Disabled by default.
- `Agent`: String advertised to clients (shown by `git clone --verbose`).
- `AuthorizeAsync`: Optional callback to allow/deny access per request. Receives the operation type (Read or Write) to distinguish between fetch/clone and push operations. By default, only read operations are allowed.
- `RepositoryNameNormalizer`: Optional callback to sanitize or transform repository names before file-system access. Applied after the `.git` suffix is removed from the repository name.
- `RepositoryResolver`: Callback to resolve the repository name from the HTTP context. By default, extracts the `repository` route parameter.
- `OnReceivePackCompleted`: Optional callback invoked after a push operation whenever at least one reference update succeeds (including partial success). The list passed to the callback contains only the successfully updated references. This allows the host application to perform cache invalidation or trigger other post-push actions.
- `RepositoryNameValidator`: Optional callback to validate repository names for security. By default only allows alphanumeric characters, hyphens, underscores, and forward slashes (no leading, trailing, or repeated slashes). Host applications can override this to allow additional characters.

### Enabling Push Operations

By default, push operations are restricted for security. To enable push, you need to:
1. Set `EnableReceivePack = true`
2. Provide an `AuthorizeAsync` callback that allows write operations for authorized users

```csharp
builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.EnableReceivePack = true;
    options.AuthorizeAsync = async (context, repositoryName, operation, cancellationToken) =>
    {
        // Allow read for everyone
        if (operation == GitOperation.Read)
            return true;
        
        // Only allow write for authenticated users
        return context.User.Identity?.IsAuthenticated == true;
    };
});
```

## Push Notification

When push operations are enabled, you can be notified when a push completes (including partial success) to invalidate application caches or trigger webhooks:

```csharp
builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.EnableReceivePack = true;
    options.OnReceivePackCompleted = async (context, repositoryName, updatedReferences) =>
    {
        // Log the push
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Repository {Repository} received push updating {Count} references", 
            repositoryName, updatedReferences.Count);
        
        // Invalidate cache
        var cache = context.RequestServices.GetRequiredService<IMyRepositoryCache>();
        await cache.InvalidateAsync(repositoryName);
        
        // Trigger webhook
        foreach (var reference in updatedReferences)
        {
            logger.LogInformation("  Updated: {Reference}", reference);
        }
    };
});
```

## Custom Repository Resolution

By default, `MapGitSmartHttp` expects a route with a `{repository}` parameter. You can customize this to use multiple parameters, no parameters (for single-repository hosting), or any custom logic:

### Multiple Parameters
```csharp
builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.RepositoryResolver = context =>
    {
        var org = context.Request.RouteValues["organization"]?.ToString();
        var repo = context.Request.RouteValues["repository"]?.ToString();
        return string.IsNullOrEmpty(org) || string.IsNullOrEmpty(repo) 
            ? null 
            : $"{org}/{repo}";
    };
});

app.MapGitSmartHttp("/git/{organization}/{repository}.git");
```

### Single Repository (No Parameters)
```csharp
builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.RepositoryResolver = context => "my-repo";
});

app.MapGitSmartHttp("/git");
```

### From Query String or Header
```csharp
builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.RepositoryResolver = context =>
    {
        // Try query string first, then header
        return context.Request.Query["repo"].FirstOrDefault() 
            ?? context.Request.Headers["X-Git-Repository"].FirstOrDefault();
    };
});

app.MapGitSmartHttp("/git");
```

## Features

- Smart HTTP compatibility for Git clients (advertise, upload-pack, receive-pack).
- SHA-1 and SHA-256 repository support through `Pmad.Git.LocalRepositories`.
- No dependency on the `git` CLI at runtime.
- Extensible authorization through user-provided callbacks.
- Pluggable background repository synchronization via `IGitRepositorySynchronizerService`.

## Background Synchronization Service

`Pmad.Git.HttpServer` includes `IGitRepositorySynchronizerService` to manage and cache background `GitRepositorySynchronizer` instances for hosted repositories. It uses a factory pattern to guarantee that synchronizers always wrap the server's canonical, managed `IGitRepository` instance (ensuring shared cache invalidation and reference locks).

### Registering the Service

```csharp
builder.Services.AddGitRepositorySynchronizerService();
```

### Setting Up a Synchronizer on an Existing Repository

```csharp
var syncService = app.Services.GetRequiredService<IGitRepositorySynchronizerService>();

// Using Pmad.Git.RemoteClient (100% managed C#, zero CLI):
syncService.SetupSynchronizer("/srv/git/my-repo.git", repo => repo.CreateSynchronizer(new GitRemoteClientSyncOptions
{
    Url = "https://upstream-git.example.com/my-repo.git",
    Credentials = GitHttpCredentials.PersonalAccessToken("token"),
    PushDebounceDelay = TimeSpan.FromSeconds(30),
    PullInterval = TimeSpan.FromMinutes(5)
}));

// Or using Pmad.Git.Cli (when native git executable is available):
syncService.SetupSynchronizer("/srv/git/my-repo.git", repo => repo.CreateSynchronizer(new GitCliSyncOptions
{
    PushDebounceDelay = TimeSpan.FromSeconds(30),
    PullInterval = TimeSpan.FromMinutes(5)
}));
```

### Initial Clone and Setup (`SetupSynchronizerAsync`)

`SetupSynchronizerAsync` ensures the local repository exists before setting up the synchronizer. If the repository directory does not exist yet (or is empty), it invokes the provided `cloneAsync` delegate to clone the repository first:

```csharp
var syncService = app.Services.GetRequiredService<IGitRepositorySynchronizerService>();

// Initial clone + sync using Pmad.Git.RemoteClient:
var synchronizer = await syncService.SetupSynchronizerAsync(
    repositoryPath: "/srv/git/my-repo",
    cloneAsync: (targetPath, ct) => GitRemoteClientRepository.CloneAsync(
        "https://upstream-git.example.com/my-repo.git",
        targetPath,
        new GitRemoteClientOptions { Credentials = GitHttpCredentials.PersonalAccessToken("token") },
        ct),
    synchronizerFactory: repo => repo.CreateSynchronizer(new GitRemoteClientSyncOptions
    {
        Url = "https://upstream-git.example.com/my-repo.git",
        Credentials = GitHttpCredentials.PersonalAccessToken("token"),
        PushDebounceDelay = TimeSpan.FromSeconds(30),
        PullInterval = TimeSpan.FromMinutes(5)
    }));

// Or initial clone + sync using Pmad.Git.Cli:
var synchronizer = await syncService.SetupSynchronizerAsync(
    repositoryPath: "/srv/git/my-repo",
    cloneAsync: (targetPath, ct) => GitCliRepository.CloneAsync(
        "https://upstream-git.example.com/my-repo.git",
        targetPath,
        cancellationToken: ct),
    synchronizerFactory: repo => repo.CreateSynchronizer(new GitCliSyncOptions
    {
        PushDebounceDelay = TimeSpan.FromSeconds(30),
        PullInterval = TimeSpan.FromMinutes(5)
    }));
```

### Cache Management & Invalidation

- `GetSynchronizerByPath(path)`: Retrieves the cached synchronizer for a repository path, or `null` if none is configured.
- `InvalidateSynchronizer(path)`: Disposes and evicts the synchronizer for a specific repository.
- `DisposeAsync()`: Disposes all cached synchronizers when the application stops.



