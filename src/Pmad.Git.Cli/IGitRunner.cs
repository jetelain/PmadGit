namespace Pmad.Git.Cli;

internal interface IGitRunner
{
    string GitCliPath { get; }

    Task<GitResponse> RunGit(string workingDirectory, string[] arguments, CancellationToken cancellationToken);
}