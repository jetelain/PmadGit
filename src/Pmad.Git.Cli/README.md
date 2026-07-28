# Pmad.Git.Cli

`Pmad.Git.Cli` is a lightweight .NET 8 library that wraps calls to the Git command-line interface (CLI) for managing Git repositories. It provides a simple API for executing Git commands and retrieving their output, making it easier to integrate Git functionality into .NET applications without directly invoking shell commands.

It is intended to allow more advanced Git operations that are not supported by `Pmad.Git.LocalRepositories`, such as pushing and pulling from remote repositories, managing branches, and handling merge conflicts.