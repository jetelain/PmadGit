namespace Pmad.Git.LocalRepositories
{
    public class GitFileConflictException : InvalidOperationException
    {
        public GitFileConflictException(string message, string filePath)
            : base(message)
        {
            FilePath = filePath;
        }

        public string FilePath { get; init; }
    }
}
