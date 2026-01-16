# Pmad.Git.LocalRepositories

`Pmad.Git.LocalRepositories` is a lightweight .NET 8 library that lets you inspect local Git repositories without shelling out to the `git` executable. It can open a repository, resolve commits, enumerate trees, read blobs, and inspect file history directly from the `.git` directory. Both SHA-1 and SHA-256 object formats are supported.

## Installation

Add a project reference to `Pmad.Git.LocalRepositories` or publish it as a package and reference it like any other NuGet dependency. The library targets .NET 8 and uses C# 12 features.

## Quick Start

```csharp
using System.Text;
using Pmad.Git.LocalRepositories;

var repository = GitRepository.Open("/path/to/repo");

var head = await repository.GetCommitAsync();
Console.WriteLine($"HEAD: {head.Id} -> {head.Message}");

await foreach (var item in repository.EnumerateCommitTreeAsync(path: "src"))
{
    Console.WriteLine($"{item.Path} ({item.Entry.Kind})");
}

var fileContent = await repository.ReadFileAsync("src/Program.cs");
Console.WriteLine(Encoding.UTF8.GetString(fileContent));

await foreach (var commit in repository.EnumerateCommitsAsync())
{
    Console.WriteLine($"{commit.Id} {commit.Message}");
}
```

### Opening a repository
- `GitRepository.Open(path)` accepts either the working directory or the `.git` directory path.
- The repository must be local and fully cloned (no sparse checkout support yet).

### Reading commits and trees
- `GetCommitAsync()` resolves `HEAD` or any reference/commit hash without blocking threads.
- `EnumerateCommitsAsync()` walks parents depth-first while avoiding duplicates as an async stream.
- `EnumerateCommitTreeAsync(reference, path)` iterates the full tree or a subtree, exposing `GitTreeItem` entries asynchronously.

### Reading file contents
- `ReadFileAsync(path, reference)` returns the blob content at a path for a given commit/reference.
- `GetFileHistoryAsync(path, reference)` yields commits where the blob hash changes.

## Testing

The solution includes an xUnit test project (`tests/Pmad.Git.LocalRepositories.Test`). Tests create temporary repositories through the real `git` CLI to cover end-to-end scenarios.

Run all tests:

```bash
dotnet test
```

Ensure the `git` executable is available on the PATH when running tests.

## Limitations & roadmap
- Currently read-only (no staging/committing APIs).
- Pack files are supported for reading; writing is not implemented.
- SHA-256 repositories are supported for reading, but mixed-hash scenarios are not tested.

Contributions and issues are welcome!
