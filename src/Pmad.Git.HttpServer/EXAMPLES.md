# Pmad.Git.HttpServer - Examples

This document provides practical examples of how to use `Pmad.Git.HttpServer` with different routing patterns.

## Default Configuration (Single Route Parameter)

The simplest configuration uses a single `{repository}` parameter in the route:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
});

var app = builder.Build();

// Default pattern: /git/{repository}.git
app.MapGitSmartHttp();

app.Run();
```

**Supported URLs:**
- `git clone http://localhost/git/myrepo.git`
- `git clone http://localhost/git/team/project.git`

## Organization and Repository Pattern

Host multiple organizations with repositories:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.RepositoryResolver = context =>
    {
        var org = context.Request.RouteValues["organization"]?.ToString();
        var repo = context.Request.RouteValues["repository"]?.ToString();
        
        if (string.IsNullOrEmpty(org) || string.IsNullOrEmpty(repo))
            return null;
            
        return $"{org}/{repo}";
    };
});

var app = builder.Build();

app.MapGitSmartHttp("/git/{organization}/{repository}.git");

app.Run();
```

**Physical Structure:**
```
/srv/git/
  acme/
    project1.git/
    project2.git/
  fabrikam/
    app.git/
```

**Supported URLs:**
- `git clone http://localhost/git/acme/project1.git`
- `git clone http://localhost/git/fabrikam/app.git`

## Catch-All Pattern for Deep Hierarchies

Support arbitrary depth repository paths:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.RepositoryResolver = context =>
    {
        // The catch-all parameter captures everything
        return context.Request.RouteValues["**path"]?.ToString();
    };
});

var app = builder.Build();

app.MapGitSmartHttp("/git/{**path}.git");

app.Run();
```

**Physical Structure:**
```
/srv/git/
  company/
    division/
      team/
        project.git/
```

**Supported URLs:**
- `git clone http://localhost/git/company/division/team/project.git`

## Single Repository Application

Host a single repository without requiring a parameter:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.RepositoryResolver = context => "website";
});

var app = builder.Build();

app.MapGitSmartHttp("/git");

app.Run();
```

**Physical Structure:**
```
/srv/git/
  website.git/
```

**Supported URLs:**
- `git clone http://localhost/git`

## Query String Based Resolution

Resolve repository from query string (useful for legacy systems):

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.RepositoryResolver = context =>
    {
        return context.Request.Query["repo"].FirstOrDefault();
    };
});

var app = builder.Build();

app.MapGitSmartHttp("/git");

app.Run();
```

**Supported URLs:**
- `git clone http://localhost/git?repo=myrepo`

**Note:** Git clients may have issues with query strings. This approach is mainly for custom tooling.

## Header-Based Resolution

Resolve repository from a custom header (for API gateways):

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.RepositoryResolver = context =>
    {
        return context.Request.Headers["X-Git-Repository"].FirstOrDefault();
    };
});

var app = builder.Build();

app.MapGitSmartHttp("/git");

app.Run();
```

This is useful when hosting behind an API gateway that sets headers based on routing rules.

## Multi-Tenancy with Subdomain

Combine subdomain extraction with repository path:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.RepositoryResolver = context =>
    {
        var host = context.Request.Host.Host;
        var tenant = host.Split('.')[0]; // Extract subdomain
        var repo = context.Request.RouteValues["repository"]?.ToString();
        
        if (string.IsNullOrEmpty(repo))
            return null;
            
        return $"{tenant}/{repo}";
    };
});

var app = builder.Build();

app.MapGitSmartHttp("/git/{repository}.git");

app.Run();
```

**Physical Structure:**
```
/srv/git/
  acme/
    project.git/
  fabrikam/
    app.git/
```

**Supported URLs:**
- `git clone http://acme.example.com/git/project.git` ? `/srv/git/acme/project.git`
- `git clone http://fabrikam.example.com/git/app.git` ? `/srv/git/fabrikam/app.git`

## Combined with Authorization

Use custom resolution with authorization:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.EnableReceivePack = true; // Enable push support
    options.RepositoryResolver = context =>
    {
        var org = context.Request.RouteValues["organization"]?.ToString();
        var repo = context.Request.RouteValues["repository"]?.ToString();
        return string.IsNullOrEmpty(org) || string.IsNullOrEmpty(repo) 
            ? null 
            : $"{org}/{repo}";
    };
    options.AuthorizeAsync = async (context, repositoryName, operation, cancellationToken) =>
    {
        // Split back to get organization
        var parts = repositoryName.Split('/');
        if (parts.Length != 2)
            return false;
            
        var org = parts[0];
        var user = context.User;
        
        // Check if user has access to this organization
        if (user.Identity?.IsAuthenticated != true || !user.HasClaim("org", org))
            return false;
        
        // Allow read for all authenticated users in the org
        if (operation == GitOperation.Read)
            return true;
        
        // Only allow write for users with write permission
        return user.HasClaim("org-write", org);
    };
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGitSmartHttp("/git/{organization}/{repository}.git");

app.Run();
```

## Read-Only Repository Access

Allow anyone to read but restrict push operations:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.EnableReceivePack = true;
    options.AuthorizeAsync = async (context, repositoryName, operation, cancellationToken) =>
    {
        // Allow all read operations (clone, fetch)
        if (operation == GitOperation.Read)
            return true;
        
        // Only allow write operations (push) for authenticated users
        return context.User.Identity?.IsAuthenticated == true;
    };
});

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGitSmartHttp();

app.Run();
```

## Repository Name Normalization

Combine custom resolution with normalization:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.RepositoryResolver = context =>
    {
        return context.Request.RouteValues["repository"]?.ToString();
    };
    options.RepositoryNameNormalizer = name =>
    {
        // Convert to lowercase and replace spaces with dashes
        return name.ToLowerInvariant().Replace(' ', '-');
    };
});

var app = builder.Build();

app.MapGitSmartHttp("/git/{repository}.git");

app.Run();
```

This allows URLs like:
- `git clone http://localhost/git/My%20Project.git` ? resolves to `my-project.git`

## Custom Repository Name Validation

Override the default validator to allow additional characters such as dots (e.g. for version-tagged repositories):

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGitSmartHttp(options =>
{
    options.RepositoryRoot = "/srv/git";
    options.RepositoryNameValidator = name =>
    {
        // Allow alphanumeric, hyphens, underscores, dots, and forward slashes
        // (still disallow leading, trailing, or repeated slashes to prevent traversal)
        return !string.IsNullOrEmpty(name)
            && !name.StartsWith('/')
            && !name.EndsWith('/')
            && !name.Contains("//")
            && name.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == '/');
    };
});

var app = builder.Build();

app.MapGitSmartHttp();

app.Run();
```

**Supported URLs:**
- `git clone http://localhost/git/project.v2.git`
- `git clone http://localhost/git/org/app.v1.0.git`

