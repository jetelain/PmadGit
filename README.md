# Pmad.Git

## Pmad.Git.LocalRepositories

`Pmad.Git.LocalRepositories` is a lightweight .NET library that lets you inspect local Git repositories, and do basic commit operations. See [Pmad.Git.LocalRepositories](src/Pmad.Git.LocalRepositories/README.md)

## Pmad.Git.HttpServer

`Pmad.Git.HttpServer` is an ASP.NET Core library that lets git synchronize with a server side stored local repository using the Git Smart HTTP protocol. See [Pmad.Git.HttpServer](src/Pmad.Git.HttpServer/README.md)

## Pmad.Git.Cli

`Pmad.Git.Cli` is a lightweight .NET library that wraps calls to the Git command-line interface (CLI) for advanced operations not supported by `Pmad.Git.LocalRepositories`, such as pushing/pulling from a remote, managing branches and resolving merge conflicts. It also provides `GitRepositorySynchronizer`, which automatically keeps a local `IGitRepository` synchronized with a remote using debounced push-on-change and periodic (or on-demand) pull, exposing a state machine to detect and resolve merge conflicts. See [Pmad.Git.Cli](src/Pmad.Git.Cli/README.md)

# License

MIT Licensed and open source.

# Related

Based on Pmad.Git :
- [Pmad.Wiki](https://github.com/jetelain/PmadWiki/) is an ASP.NET Core library that lets host a git/markdown based wiki into an MVC application.