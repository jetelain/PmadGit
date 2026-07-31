namespace Pmad.Git.Tests.Infrastructure;

public static class GitTestHelper
{
    public static string GetHeadReference(GitTestRepository repo)
    {
        var headPath = Path.Combine(repo.GitDirectory, "HEAD");
        var content = File.ReadAllText(headPath).Trim();
        if (!content.StartsWith("ref: ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("HEAD is not pointing to a symbolic reference");
        }
        return content[5..].Trim();
    }

    public static string GetDefaultBranch(GitTestRepository repo)
    {
        var headContent = File.ReadAllText(Path.Combine(repo.GitDirectory, "HEAD")).Trim();
        if (headContent.StartsWith("ref: refs/heads/"))
        {
            return headContent.Substring("ref: refs/heads/".Length);
        }
        return "main";
    }

    public static string RunGit(string workingDirectory, string arguments)
    {
        return TestHelper.RunGit(workingDirectory, arguments);
    }

    public static void TryDeleteDirectory(string path)
    {
        TestHelper.TryDeleteDirectory(path);
    }
}
