using System.Diagnostics;

namespace Pmad.Git.LocalRepositories.Test.Infrastructure
{
    internal static class GitTestHelper
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
    }
}
