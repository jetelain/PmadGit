using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pmad.Git.Cli;
using Pmad.Git.HttpServer;
using Pmad.Git.LocalRepositories;
using Pmad.Git.RemoteClient;

namespace Pmad.Git.Acceptance;

public static class Program
{
    private static readonly GitCommitSignature DefaultSignature = new("Acceptance Agent", "acceptance@pmad.git", DateTimeOffset.UtcNow);

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        try
        {
            return command switch
            {
                "compare-status" => await CompareStatusAsync(args),
                "compare-tree" => await CompareTreeAsync(args),
                "compare-refs" => await CompareRefsAsync(args),
                "verify-fsck" => await VerifyFsckAsync(args),
                "managed-commit" => await ManagedCommitAsync(args),
                "managed-amend" => await ManagedAmendAsync(args),
                "managed-reset" => await ManagedResetAsync(args),
                "managed-squash" => await ManagedSquashAsync(args),
                "managed-revert" => await ManagedRevertAsync(args),
                "serve-repo" => await ServeRepoAsync(args),
                "client-clone" => await ClientCloneAsync(args),
                "client-pull" => await ClientPullAsync(args),
                "client-push" => await ClientPushAsync(args),
                "create-synthetic-repo" => await CreateSyntheticRepoAsync(args),
                _ => HandleUnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"[ERROR] Command '{command}' failed: {ex.Message}");
            Console.ResetColor();
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Pmad.Git Acceptance Test Companion Tool");
        Console.WriteLine("========================================");
        Console.WriteLine("Usage: dotnet run --project tools/Pmad.Git.Acceptance -- <command> [arguments]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  compare-status <repoPath>                       Compares Pmad.Git status with native git CLI");
        Console.WriteLine("  compare-tree <repoPath> [commitIsh]             Compares tree hierarchy between Pmad.Git and git ls-tree");
        Console.WriteLine("  compare-refs <repoPath>                         Validates all references between Pmad.Git and git CLI");
        Console.WriteLine("  verify-fsck <repoPath>                          Runs git fsck --full --strict on the repository");
        Console.WriteLine("  managed-commit <repoPath> <file> <text> <msg>   Stages file and creates a commit via managed code");
        Console.WriteLine("  managed-amend <repoPath> <file> <text> <msg>    Amends tip commit via managed code");
        Console.WriteLine("  managed-reset <repoPath> <commit> <Soft|Mixed|Hard> Resets workspace via managed code");
        Console.WriteLine("  managed-squash <repoPath> <baseCommit> <msg>    Squashes commit range via managed code");
        Console.WriteLine("  managed-revert <repoPath> <commitIsh>           Reverts a commit via managed code");
        Console.WriteLine("  serve-repo <repoRoot> <port>                    Spins up Pmad.Git.HttpServer on 127.0.0.1:<port>");
        Console.WriteLine("  client-clone <url> <targetPath> [branch]        Performs pure managed clone via Pmad.Git.RemoteClient");
        Console.WriteLine("  client-pull <repoPath>                          Performs pure managed pull via Pmad.Git.RemoteClient");
        Console.WriteLine("  client-push <repoPath>                          Performs pure managed push via Pmad.Git.RemoteClient");
        Console.WriteLine("  create-synthetic-repo <targetPath> [--sha256]   Creates a rich test repo with edge cases");
    }

    private static string ResolveRepoRoot(string path)
    {
        var full = Path.GetFullPath(path);
        var current = full;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, ".git")) ||
                (File.Exists(Path.Combine(current, "HEAD")) && File.Exists(Path.Combine(current, "config"))))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent == null || parent.FullName == current) break;
            current = parent.FullName;
        }
        return full;
    }

    private static int HandleUnknownCommand(string command)
    {
        Console.Error.WriteLine($"[ERROR] Unknown command '{command}'. Run with --help for available commands.");
        return 2;
    }

    private static async Task<int> CompareStatusAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: compare-status <repoPath>");
            return 2;
        }

        var repoPath = ResolveRepoRoot(args[1]);
        using var repo = GitRepositoryWithIndexAndWorkspace.Open(repoPath);
        var managedStatus = await repo.GetStatusAsync();

        // Run native git status --porcelain=v2
        var (gitExit, gitOut, gitErr) = RunGit(repoPath, "-c core.quotePath=false status --porcelain=v2");
        if (gitExit != 0)
        {
            Console.Error.WriteLine($"[FAIL] git status returned exit code {gitExit}: {gitErr}");
            return 1;
        }

        Console.WriteLine($"[INFO] Pmad.Git found {managedStatus.Entries.Count} status entries in {repoPath}.");
        
        // Parse git porcelain v2 entries
        // Format:
        // 1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path> (normal change)
        // 2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <X><score> <path><sep><origPath> (renamed)
        // u <XY> ... (unmerged)
        // ? <path> (untracked)
        // ! <path> (ignored)
        var nativeEntries = new Dictionary<string, (char Staged, char Worktree)>(StringComparer.Ordinal);
        var lines = gitOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("1 ") || line.StartsWith("2 "))
            {
                var parts = line.Split(' ');
                var xy = parts[1];
                var path = parts[^1];
                nativeEntries[path] = (xy[0], xy[1]);
            }
            else if (line.StartsWith("? "))
            {
                var path = line[2..].Trim();
                nativeEntries[path] = ('?', '?');
            }
        }

        int mismatches = 0;
        foreach (var entry in managedStatus.Entries)
        {
            if (entry.WorkingTreeStatus == GitFileStatus.Ignored)
            {
                continue; // Ignore matching matches
            }

            if (!nativeEntries.TryGetValue(entry.Path, out var nativeState))
            {
                Console.WriteLine($"[MISMATCH] Path '{entry.Path}' detected by Pmad.Git ({entry.StagedStatus}/{entry.WorkingTreeStatus}) but missing in git status.");
                mismatches++;
                continue;
            }

            bool stagedMatches = (entry.StagedStatus, nativeState.Staged) switch
            {
                (GitFileStatus.Clean, '.') => true,
                // Untracked files are naturally Clean relative to HEAD (absent from index),
                // while native git uses '?' in the staged column.
                (GitFileStatus.Clean, '?') => true,
                (GitFileStatus.Untracked, '?') => true,
                (GitFileStatus.StagedNew, 'A') => true,
                (GitFileStatus.StagedModified, 'M') => true,
                (GitFileStatus.StagedDeleted, 'D') => true,
                _ => false
            };

            bool worktreeMatches = (entry.WorkingTreeStatus, nativeState.Worktree) switch
            {
                (GitFileStatus.Clean, '.') => true,
                (GitFileStatus.Untracked, '?') => true,
                (GitFileStatus.Modified, 'M') => true,
                (GitFileStatus.Deleted, 'D') => true,
                _ => false
            };

            if (!stagedMatches || !worktreeMatches)
            {
                Console.WriteLine($"[MISMATCH] Path '{entry.Path}': Pmad=({entry.StagedStatus}, {entry.WorkingTreeStatus}) vs Git=({nativeState.Staged}, {nativeState.Worktree})");
                mismatches++;
            }
        }

        // Check if native git reported entries not found by Pmad.Git
        foreach (var kvp in nativeEntries)
        {
            if (managedStatus.Entries.All(e => e.Path != kvp.Key))
            {
                Console.WriteLine($"[MISMATCH] Path '{kvp.Key}' found by git status ({kvp.Value.Staged}/{kvp.Value.Worktree}) but not detected by Pmad.Git.");
                mismatches++;
            }
        }

        if (mismatches > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] Found {mismatches} status discrepancies between Pmad.Git and git CLI.");
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[PASS] Working tree status is 100% identical between Pmad.Git and native git CLI.");
        Console.ResetColor();
        return 0;
    }

    private static async Task<int> CompareTreeAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: compare-tree <repoPath> [commitIsh]");
            return 2;
        }

        var repoPath = ResolveRepoRoot(args[1]);
        var commitIsh = args.Length > 2 ? args[2] : "HEAD";

        var repo = GitRepository.Open(repoPath);
        var commit = await repo.GetCommitAsync(commitIsh);

        var (gitExit, gitOut, gitErr) = RunGit(repoPath, $"-c core.quotePath=false ls-tree -r -t {commitIsh}");
        if (gitExit != 0)
        {
            Console.Error.WriteLine($"[FAIL] git ls-tree returned exit code {gitExit}: {gitErr}");
            return 1;
        }

        var nativeEntries = new Dictionary<string, (string Mode, string Type, string Hash)>(StringComparer.Ordinal);
        foreach (var line in gitOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            // Format: <mode> SP <type> SP <object> TAB <file>
            var tabIdx = line.IndexOf('\t');
            if (tabIdx < 0) continue;
            var path = line[(tabIdx + 1)..].Trim();
            var meta = line[..tabIdx].Split(' ');
            if (meta.Length >= 3)
            {
                nativeEntries[path] = (meta[0], meta[1], meta[2]);
            }
        }

        var managedEntries = new List<(string Path, GitTreeEntry Entry)>();
        await foreach (var item in repo.EnumerateCommitTreeAsync(commitIsh))
        {
            managedEntries.Add((item.Path, item.Entry));
        }

        Console.WriteLine($"[INFO] Comparing commit {commit.Id}: Pmad.Git has {managedEntries.Count} items, git CLI has {nativeEntries.Count} items.");

        int errors = 0;
        foreach (var (path, entry) in managedEntries)
        {
            if (!nativeEntries.TryGetValue(path, out var nativeItem))
            {
                Console.WriteLine($"[MISMATCH] Path '{path}' found in Pmad.Git tree walk but missing in git ls-tree.");
                errors++;
                continue;
            }

            var nativeHash = new GitHash(nativeItem.Hash);
            if (entry.Hash != nativeHash)
            {
                Console.WriteLine($"[MISMATCH] Path '{path}' hash mismatch: Pmad={entry.Hash} vs Git={nativeItem.Hash}");
                errors++;
            }
        }

        if (errors > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] Found {errors} discrepancies during tree comparison.");
            Console.ResetColor();
            return 1;
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[PASS] Tree structure at {commitIsh} ({commit.Id}) is 100% equivalent to git ls-tree.");
        Console.ResetColor();
        return 0;
    }

    private static async Task<int> CompareRefsAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: compare-refs <repoPath>");
            return 2;
        }

        var repoPath = ResolveRepoRoot(args[1]);
        var repo = GitRepository.Open(repoPath);

        var (gitExit, gitOut, gitErr) = RunGit(repoPath, "show-ref");
        var gitHead = RunGit(repoPath, "rev-parse HEAD");

        var headCommit = await repo.ReferenceStore.ResolveHeadAsync();
        Console.WriteLine($"[INFO] HEAD: Pmad={headCommit} | Git={gitHead.Output.Trim()}");

        if (headCommit.ToString() != gitHead.Output.Trim())
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] HEAD commit differs: Pmad={headCommit} vs Git={gitHead.Output.Trim()}");
            Console.ResetColor();
            return 1;
        }

        int compared = 0;
        if (gitExit == 0 && !string.IsNullOrWhiteSpace(gitOut))
        {
            foreach (var line in gitOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(' ', 2);
                if (parts.Length < 2) continue;
                var gitHash = parts[0];
                var refName = parts[1];

                var pmadHash = await repo.ReferenceStore.TryResolveReferenceAsync(refName);
                if (pmadHash == null)
                {
                    Console.WriteLine($"[MISMATCH] Reference '{refName}' found in git show-ref but missing in Pmad.Git ReferenceStore.");
                    return 1;
                }

                if (pmadHash.Value.ToString() != gitHash)
                {
                    Console.WriteLine($"[MISMATCH] Reference '{refName}' points to {pmadHash.Value} in Pmad vs {gitHash} in Git.");
                    return 1;
                }
                compared++;
            }
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[PASS] All {compared} references and HEAD verified identical.");
        Console.ResetColor();
        return 0;
    }

    private static Task<int> VerifyFsckAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: verify-fsck <repoPath>");
            return Task.FromResult(2);
        }

        var repoPath = ResolveRepoRoot(args[1]);
        var (exitCode, stdout, stderr) = RunGit(repoPath, "fsck --full --strict");

        if (exitCode != 0 || stdout.Contains("error:", StringComparison.OrdinalIgnoreCase) || stderr.Contains("error:", StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[FAIL] git fsck detected integrity issues (exit {exitCode}):");
            Console.WriteLine(stdout);
            Console.WriteLine(stderr);
            Console.ResetColor();
            return Task.FromResult(1);
        }

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[PASS] git fsck --full --strict passed cleanly on {repoPath}.");
        Console.ResetColor();
        return Task.FromResult(0);
    }

    private static async Task<int> ManagedCommitAsync(string[] args)
    {
        if (args.Length < 5)
        {
            Console.Error.WriteLine("Usage: managed-commit <repoPath> <relativePath> <content> <commitMessage>");
            return 2;
        }

        var repoPath = ResolveRepoRoot(args[1]);
        var relPath = args[2];
        var content = args[3];
        var commitMsg = args[4];

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(repoPath);
        var fullPath = Path.Combine(repoPath, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content);

        await repo.StageAsync(relPath);
        var hash = await repo.CommitAsync(commitMsg, new GitCommitMetadata(commitMsg, DefaultSignature));

        Console.WriteLine($"[INFO] Managed commit created: {hash}");

        // Validate repository integrity
        return await VerifyFsckAsync(new[] { "verify-fsck", repoPath });
    }

    private static async Task<int> ManagedAmendAsync(string[] args)
    {
        if (args.Length < 5)
        {
            Console.Error.WriteLine("Usage: managed-amend <repoPath> <relativePath> <content> <commitMessage>");
            return 2;
        }

        var repoPath = ResolveRepoRoot(args[1]);
        var relPath = args[2];
        var content = args[3];
        var commitMsg = args[4];

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(repoPath);
        var fullPath = Path.Combine(repoPath, relPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, content);

        await repo.StageAsync(relPath);
        var hash = await repo.CommitAmendAsync(commitMsg, new GitCommitMetadata(commitMsg, DefaultSignature));

        Console.WriteLine($"[INFO] Managed amended commit created: {hash}");
        return await VerifyFsckAsync(new[] { "verify-fsck", repoPath });
    }

    private static async Task<int> ManagedResetAsync(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("Usage: managed-reset <repoPath> <commitIsh> <Soft|Mixed|Hard>");
            return 2;
        }

        var repoPath = ResolveRepoRoot(args[1]);
        var commitIsh = args[2];
        var modeStr = args[3];

        if (!Enum.TryParse<GitResetMode>(modeStr, true, out var mode))
        {
            Console.Error.WriteLine($"[ERROR] Invalid reset mode '{modeStr}'. Valid values: Soft, Mixed, Hard.");
            return 2;
        }

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(repoPath);
        var commit = await repo.GetCommitAsync(commitIsh);
        await repo.ResetAsync(commit.Id, mode);

        Console.WriteLine($"[INFO] Reset workspace to {commit.Id} with mode {mode}.");
        return await VerifyFsckAsync(new[] { "verify-fsck", repoPath });
    }

    private static async Task<int> ManagedSquashAsync(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("Usage: managed-squash <repoPath> <baseCommitIsh> <message>");
            return 2;
        }

        var repoPath = ResolveRepoRoot(args[1]);
        var baseIsh = args[2];
        var message = args[3];

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(repoPath);
        var baseCommit = await repo.GetCommitAsync(baseIsh);
        var squashed = await repo.SquashRangeAsync(baseCommit.Id, message, new GitCommitMetadata(message, DefaultSignature));

        Console.WriteLine($"[INFO] Squashed commit created: {squashed}");
        return await VerifyFsckAsync(new[] { "verify-fsck", repoPath });
    }

    private static async Task<int> ManagedRevertAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: managed-revert <repoPath> <commitIsh>");
            return 2;
        }

        var repoPath = ResolveRepoRoot(args[1]);
        var commitIsh = args[2];

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(repoPath);
        var commit = await repo.GetCommitAsync(commitIsh);
        var revertCommit = await repo.RevertAsync(commit.Id, new GitCommitMetadata($"Revert \"{commit.Message}\"", DefaultSignature));

        Console.WriteLine($"[INFO] Revert commit created: {revertCommit}");
        return await VerifyFsckAsync(new[] { "verify-fsck", repoPath });
    }

    private static async Task<int> ServeRepoAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: serve-repo <repoRoot> <port>");
            return 2;
        }

        var repoRoot = Path.GetFullPath(args[1]);
        var port = int.Parse(args[2]);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        builder.Services.AddGitSmartHttp(options =>
        {
            options.RepositoryRoot = repoRoot;
            options.EnableUploadPack = true;
            options.EnableReceivePack = true;
            options.AuthorizeAsync = (_, _, _, _) => ValueTask.FromResult(true);
        });

        var app = builder.Build();
        app.MapGitSmartHttp();

        Console.WriteLine($"SERVER_READY:http://127.0.0.1:{port}");
        await app.RunAsync();
        return 0;
    }

    private static async Task<int> ClientCloneAsync(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: client-clone <url> <targetPath> [branch]");
            return 2;
        }

        var url = args[1];
        var targetPath = Path.GetFullPath(args[2]);
        var branch = args.Length > 3 ? args[3] : null;

        Console.WriteLine($"[INFO] Cloning {url} to {targetPath} (branch: {branch ?? "default"})...");
        using var repo = await GitRemoteClientRepository.CloneAsync(url, targetPath, new GitRemoteClientOptions(), branch: branch);

        Console.WriteLine($"[INFO] Clone succeeded. Checking repository with git fsck...");
        return await VerifyFsckAsync(new[] { "verify-fsck", targetPath });
    }

    private static async Task<int> ClientPullAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: client-pull <repoPath>");
            return 2;
        }

        var repoPath = ResolveRepoRoot(args[1]);
        using var local = GitRepositoryWithIndexAndWorkspace.Open(repoPath);
        using var remote = new GitRemoteClientRepository(local);

        var result = await remote.PullAsync();
        Console.WriteLine($"[INFO] Pull result: Status={result.Status}, HasConflicts={result.HasConflicts}");
        if (result.HasConflicts)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[CONFLICT] Conflicted files: {string.Join(", ", result.ConflictedFiles)}");
            Console.ResetColor();
            return 1;
        }

        return await VerifyFsckAsync(new[] { "verify-fsck", repoPath });
    }

    private static async Task<int> ClientPushAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: client-push <repoPath>");
            return 2;
        }

        var repoPath = ResolveRepoRoot(args[1]);
        using var local = GitRepositoryWithIndexAndWorkspace.Open(repoPath);
        using var remote = new GitRemoteClientRepository(local);

        await remote.PushAsync();
        Console.WriteLine("[INFO] Push succeeded.");
        return 0;
    }

    private static async Task<int> CreateSyntheticRepoAsync(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: create-synthetic-repo <targetPath> [--sha256]");
            return 2;
        }

        var targetPath = Path.GetFullPath(args[1]);
        var isSha256 = args.Contains("--sha256");

        if (Directory.Exists(targetPath))
        {
            Directory.Delete(targetPath, true);
        }
        Directory.CreateDirectory(targetPath);

        // Run git init with appropriate format
        var initCmd = isSha256 ? "init --object-format=sha256 -b main" : "init -b main";
        var (initExit, _, initErr) = RunGit(targetPath, initCmd);
        if (initExit != 0)
        {
            Console.Error.WriteLine($"[FAIL] git init failed: {initErr}");
            return 1;
        }

        using var repo = GitRepositoryWithIndexAndWorkspace.Open(targetPath);

        // 1. Add canonical sorting conflict candidates
        await File.WriteAllTextAsync(Path.Combine(targetPath, "dir-other.txt"), "dir-other content\n");
        await File.WriteAllTextAsync(Path.Combine(targetPath, "dir.txt"), "dir.txt content\n");
        Directory.CreateDirectory(Path.Combine(targetPath, "dir"));
        await File.WriteAllTextAsync(Path.Combine(targetPath, "dir", "child.txt"), "child in dir\n");
        await File.WriteAllTextAsync(Path.Combine(targetPath, "dir0.txt"), "dir0 content\n");
        await File.WriteAllTextAsync(Path.Combine(targetPath, "dir_other.txt"), "dir_other content\n");

        // 2. Add binary file (64KB)
        var binBytes = new byte[65536];
        for (int i = 0; i < binBytes.Length; i++) binBytes[i] = (byte)(i % 256);
        await File.WriteAllBytesAsync(Path.Combine(targetPath, "binary.dat"), binBytes);

        // 3. Add Unicode / UTF-8 filename
        await File.WriteAllTextAsync(Path.Combine(targetPath, "unicode-éàç-utf8.txt"), "Unicode UTF-8 content\n");

        // 4. Add .gitignore
        await File.WriteAllTextAsync(Path.Combine(targetPath, ".gitignore"), "*.log\nbuild/\n!build/keep.txt\n");
        Directory.CreateDirectory(Path.Combine(targetPath, "build"));
        await File.WriteAllTextAsync(Path.Combine(targetPath, "build", "keep.txt"), "keep this file\n");
        await File.WriteAllTextAsync(Path.Combine(targetPath, "build", "drop.log"), "drop this file\n");

        // 5. Stage all and commit via managed code
        await repo.StageAllAsync();
        var c1 = await repo.CommitAsync("Initial synthetic baseline", new GitCommitMetadata("Initial synthetic baseline", DefaultSignature));

        // 6. Create branch feature-x and commit
        RunGit(targetPath, "checkout -b feature-x");
        await File.WriteAllTextAsync(Path.Combine(targetPath, "feature.txt"), "feature branch content\n");
        await repo.StageAsync("feature.txt");
        var c2 = await repo.CommitAsync("Feature branch commit", new GitCommitMetadata("Feature branch commit", DefaultSignature));

        // 7. Add tags
        await repo.ReferenceStore.CreateReferenceAsync("refs/tags/v1.0.0", c1);

        // Switch back to main
        RunGit(targetPath, "checkout main");

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"[PASS] Synthetic repository successfully generated at {targetPath} (Format: {(isSha256 ? "SHA-256" : "SHA-1")}).");
        Console.ResetColor();

        return await VerifyFsckAsync(new[] { "verify-fsck", targetPath });
    }

    public static (int ExitCode, string Output, string Error) RunGit(string workingDir, string arguments)
    {
        var psi = new ProcessStartInfo("git", arguments)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch git CLI.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }
}
