namespace Pmad.Git.LocalRepositories;

internal readonly record struct TreeEntryData(string Name, int Mode, GitHash Hash);
