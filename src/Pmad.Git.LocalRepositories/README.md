# Pmad.Git.LocalRepositories

`Pmad.Git.LocalRepositories` is a lightweight .NET 8 library that lets you inspect local Git repositories, and do basic commit operations, without shelling out to the `git` executable. It can open a repository, resolve commits, enumerate trees, read blobs, and inspect file history directly from the `.git` directory. Both SHA-1 and SHA-256 object formats are supported.

## Installation

Add a project reference to `Pmad.Git.LocalRepositories` or publish it as a package and reference it like any other NuGet dependency. The library targets .NET 8 and uses C# 12 features.

## Quick Start

```csharp
using System.Text;
using Pmad.Git.LocalRepositories;

// Create a new repository
var repository = GitRepository.Init("/path/to/new-repo");

// Or open an existing repository
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

var metadata = new GitCommitMetadata(
    message: "Automated change",
    author: new GitCommitSignature(
      name: "CI Bot",
      email: "ci@example.com",
      timestamp: DateTimeOffset.UtcNow));

var commitId = await repository.CreateCommitAsync(
    branchName: "main",
    operations: [
        new AddFileOperation("src/NewFile.txt", Encoding.UTF8.GetBytes("payload"))
    ],
    metadata);

Console.WriteLine($"Created commit {commitId.Value}");
```

### Creating a new repository
- `GitRepository.Init(path, bare, initialBranch)` creates a new empty git repository at the specified location.
- Set `bare` to `true` to create a bare repository (no working directory).
- The `initialBranch` parameter defaults to "main" but can be customized.

### Opening a repository
- `GitRepository.Open(path)` accepts either the working directory or the `.git` directory path.
- The repository must be local and fully cloned (no sparse checkout support yet).

### Reading commits and trees
- `GetCommitAsync()` resolves `HEAD` or any reference/commit hash without blocking threads.
- `EnumerateCommitsAsync()` yields commits reachable from the starting reference in reverse chronological (newest-first) order as an async stream.
- `EnumerateCommitTreeAsync(reference, path, searchOption)` iterates the full tree or a subtree, exposing `GitTreeItem` entries asynchronously.

### Checking path existence
- `PathExistsAsync(path, reference)` returns `true` if a path (file or directory) exists in the given commit.
- `FileExistsAsync(filePath, reference)` returns `true` if a blob exists at the path.
- `DirectoryExistsAsync(directoryPath, reference)` returns `true` if a tree exists at the path.
- `GetPathTypeAsync(path, reference)` returns the `GitTreeEntryKind` (`Blob` or `Tree`) of the path, or `null` if it does not exist.

### Reading file contents
- `ReadFileAsync(path, reference)` returns the blob content at a path for a given commit/reference.
- `ReadFileAndHashAsync(path, reference)` returns both the blob content and its `GitHash` as a `GitFileContentAndHash`.
- `ReadFileStreamAsync(path, reference)` returns a `GitObjectStream` that exposes the blob as a `Stream` without buffering the full content for loose objects. Dispose the stream after use (supports both `using` and `await using`).
- `EnumerateFileHistoryAsync(path, reference)` yields commits where the blob hash changes.

### Querying last changes
- `GetFilesWithLastChangeAsync(reference, path, predicate, searchOption)` traverses the commit graph once and returns a sorted list of `GitFileLastChange` entries, each pairing a file path with the most recent commit that changed it. More efficient than calling `EnumerateFileHistoryAsync` per file.

### Creating commits
- `CreateCommitAsync(branch, operations, metadata)` applies operations directly to the Git object store and updates the branch reference without invoking the CLI. The method is thread-safe against concurrent commits to the same branch.
- Available operations derive from `GitCommitOperation`:
  - `AddFileOperation(path, byte[])` — adds a new file from a byte array.
  - `AddFileStreamOperation(path, Stream)` — adds a new file from a stream.
  - `UpdateFileOperation(path, byte[], expectedPreviousHash?)` — updates an existing file from a byte array.
  - `UpdateFileStreamOperation(path, Stream, expectedPreviousHash?)` — updates an existing file from a stream.
  - `RemoveFileOperation(path)` — removes an existing file.
  - `MoveFileOperation(sourcePath, destinationPath)` — moves or renames an existing file.
- `GitCommitMetadata` captures the commit message plus author/committer identity and timestamps used to build the commit object.

### Cache management
- `InvalidateCaches(clearAllData)` clears cached references and loose-object metadata so subsequent operations reflect the current on-disk state. Pass `true` to also clear structural metadata such as the pack index.

### Reachability
- `IsCommitReachableAsync(from, to)` returns `true` if the `to` commit is an ancestor of (or equal to) `from`; useful for fast-forward validation.

## Testing

The solution includes an xUnit test project (`tests/Pmad.Git.LocalRepositories.Test`). Tests create temporary repositories through the real `git` CLI to cover end-to-end scenarios.

Run all tests:

```bash
dotnet test
```

Ensure the `git` executable is available on the PATH when running tests.

## Limitations & roadmap
- Pack files are supported for reading; writing is not implemented.
- SHA-256 repositories are supported for reading, but mixed-hash scenarios are not tested.

Contributions and issues are welcome!
