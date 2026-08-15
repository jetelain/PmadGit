# Copilot Instructions

## Directives
- In Pmad.Git projects, GitSyncOptions.GitRunner is internal and used via InternalsVisibleTo for test doubles; unit tests for Cli sync classes use FakeGitRunner combined with a real disk-backed GitRepository (LocalRepositories) to trigger Changed events without invoking the real git executable, while true end-to-end integration tests use GitCliTestRepository with the real git CLI.