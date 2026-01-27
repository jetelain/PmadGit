# Pmad.Git.HttpServer

`Pmad.Git.HttpServer` is an ASP.NET Core library that lets git synchronize with a server side stored local repository using the Git Smart HTTP protocol.

## Getting Started

1. Add a reference to `Pmad.Git.HttpServer` in your ASP.NET Core application.
2. Register the endpoints inside `Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGitSmartHttp(new GitSmartHttpOptions
{
    RepositoryRoot = "/srv/git",
    EnableUploadPack = true
});

var app = builder.Build();
app.MapGitSmartHttp();
app.Run();
```

With the sample above, git clients can clone/fetch repositories stored under `/srv/git` using the Smart HTTP endpoints (`/git/{repo}/info/refs`, `/git/{repo}/git-upload-pack`).

## Configuration

`GitSmartHttpOptions` controls behavior:

- `RepositoryRoot`: Absolute path containing repositories. Mandatory.
- `EnableUploadPack`: Allows `git-upload-pack` (fetch/clone). Enabled by default.
- `EnableReceivePack`: Allows `git-receive-pack` (push). Disabled by default.
- `Agent`: String advertised to clients (shown by `git clone --verbose`).
- `AuthorizeAsync`: Optional callback to allow/deny access per request. Receives the operation type (Read or Write) to distinguish between fetch/clone and push operations.
- `RepositoryNameNormalizer`: Optional sanitizer for custom routing schemes.
- `RepositoryResolver`: Callback to resolve the repository name from the HTTP context. By default, extracts the `repository` route parameter.
- `OnReceivePackCompleted`: Optional callback invoked after a successful push operation. This allows the host application to perform cache invalidation or trigger other post-push actions.

## Push Notification

When push operations are enabled, you can be notified when a push completes successfully to invalidate application caches or trigger webhooks:

```csharp
builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.EnableReceivePack = true;
    options.OnReceivePackCompleted = async (context, repositoryName, updatedReferences, cancellationToken) =>
    {
        // Log the push
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Repository {Repository} received push updating {Count} references", 
            repositoryName, updatedReferences.Count);
        
        // Invalidate cache
        var cache = context.RequestServices.GetRequiredService<IMyRepositoryCache>();
        await cache.InvalidateAsync(repositoryName, cancellationToken);
        
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

