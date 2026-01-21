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
    EnableUploadPack = true,
    EnableReceivePack = false
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
- `EnableReceivePack`: Allows `git-receive-pack` (push). Enabled by default.
- `Agent`: String advertised to clients (shown by `git clone --verbose`).
- `AuthorizeAsync`: Optional callback to allow/deny access per request.
- `RepositoryNameNormalizer`: Optional sanitizer for custom routing schemes.

## Features

- Smart HTTP compatibility for Git clients (advertise, upload-pack, receive-pack).
- SHA-1 and SHA-256 repository support through `Pmad.Git.LocalRepositories`.
- No dependency on the `git` CLI at runtime.
- Extensible authorization through user-provided callbacks.

