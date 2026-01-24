using System.Diagnostics;

namespace Pmad.Git.LocalRepositories.Test.Infrastructure
{
    public static class GitTestHelper
    {
        internal static string GetHeadReference(GitTestRepository repo)
        {
            var headPath = Path.Combine(repo.GitDirectory, "HEAD");
            var content = File.ReadAllText(headPath).Trim();
            if (!content.StartsWith("ref: ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("HEAD is not pointing to a symbolic reference");
            }
            return content[5..].Trim();
        }

        internal static string GetDefaultBranch(GitTestRepository repo)
        {
            var headContent = File.ReadAllText(Path.Combine(repo.GitDirectory, "HEAD")).Trim();
            if (headContent.StartsWith("ref: refs/heads/"))
            {
                return headContent.Substring("ref: refs/heads/".Length);
            }
            return "main";
        }

        internal static string RunGit(string workingDirectory, string arguments)
        {
            var startInfo = new ProcessStartInfo("git", arguments)
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Unable to start git process");
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"git {arguments} failed with exit code {process.ExitCode}:{Environment.NewLine}{error}{Environment.NewLine}{output}");
            }

            return string.IsNullOrEmpty(output) ? error : output;
        }

        /// <summary>
        /// Attempts to delete a directory recursively, ignoring any exceptions.
        /// This is useful for test cleanup where failures should not affect test results.
        /// </summary>
        /// <param name="path">The directory path to delete.</param>
        internal static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch(Exception ex)
            {
                Debug.WriteLine($"Failed to delete test directory '{path}': {ex}");
                // Ignore cleanup failures in tests
            }
        }
    }
}

