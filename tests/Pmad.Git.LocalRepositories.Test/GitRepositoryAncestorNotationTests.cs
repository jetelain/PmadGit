namespace Pmad.Git.LocalRepositories.Test;

/// <summary>
/// Tests for the ancestor/parent suffix resolution in <see cref="GitRepository"/>:
/// <c>~N</c>, <c>~</c>, <c>^</c>, <c>^N</c> and chained combinations.
/// </summary>
public class GitRepositoryAncestorNotationTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Creates a linear chain of N commits and returns the hashes newest-first.</summary>
    private static (GitRepository repo, GitTestRepository testRepo, List<string> hashes) CreateLinearHistory(int extraCommits)
    {
        var testRepo = GitTestRepository.Create();
        var hashes = new List<string> { testRepo.Head.ToString() };

        for (var i = 1; i <= extraCommits; i++)
        {
            testRepo.Commit($"Commit {i}", ($"f{i}.txt", $"c{i}"));
            hashes.Insert(0, testRepo.Head.ToString()); // newest first
        }

        var repo = GitRepository.Open(testRepo.WorkingDirectory);
        return (repo, testRepo, hashes);
    }

    // ── ~N (first-parent chain) ────────────────────────────────────────────

    [Fact]
    public async Task GetCommitAsync_HeadTilde1_ReturnsFirstParent()
    {
        var (repo, testRepo, hashes) = CreateLinearHistory(1);
        using (testRepo)
        {
            // hashes[0] = HEAD (newest), hashes[1] = initial commit
            var commit = await repo.GetCommitAsync("HEAD~1");

            Assert.Equal(hashes[1], commit.Id.ToString());
        }
    }

    [Fact]
    public async Task GetCommitAsync_HeadTilde2_ReturnsGrandparent()
    {
        var (repo, testRepo, hashes) = CreateLinearHistory(2);
        using (testRepo)
        {
            var commit = await repo.GetCommitAsync("HEAD~2");

            Assert.Equal(hashes[2], commit.Id.ToString());
        }
    }

    [Fact]
    public async Task GetCommitAsync_BranchNameTilde1_ResolvesBranchThenParent()
    {
        var (repo, testRepo, hashes) = CreateLinearHistory(1);
        using (testRepo)
        {
            // "master~1" should work just like "HEAD~1" on the master branch
            var commit = await repo.GetCommitAsync("master~1");

            Assert.Equal(hashes[1], commit.Id.ToString());
        }
    }

    [Fact]
    public async Task GetCommitAsync_BareTilde_IsSameAsTilde1()
    {
        var (repo, testRepo, hashes) = CreateLinearHistory(1);
        using (testRepo)
        {
            var commit = await repo.GetCommitAsync("HEAD~");

            Assert.Equal(hashes[1], commit.Id.ToString());
        }
    }

    [Fact]
    public async Task GetCommitAsync_Tilde0_ReturnsSameCommit()
    {
        var (repo, testRepo, hashes) = CreateLinearHistory(1);
        using (testRepo)
        {
            var commit = await repo.GetCommitAsync("HEAD~0");

            Assert.Equal(hashes[0], commit.Id.ToString());
        }
    }

    // ── ^ (nth parent) ────────────────────────────────────────────────────

    [Fact]
    public async Task GetCommitAsync_Caret1_ReturnsSameAsParent()
    {
        var (repo, testRepo, hashes) = CreateLinearHistory(1);
        using (testRepo)
        {
            var commitCaret = await repo.GetCommitAsync("HEAD^1");
            var commitTilde = await repo.GetCommitAsync("HEAD~1");

            Assert.Equal(commitTilde.Id, commitCaret.Id);
        }
    }

    [Fact]
    public async Task GetCommitAsync_BareCaret_IsSameAsCaret1()
    {
        var (repo, testRepo, hashes) = CreateLinearHistory(1);
        using (testRepo)
        {
            var commitCaret = await repo.GetCommitAsync("HEAD^");
            var commitTilde = await repo.GetCommitAsync("HEAD~");

            Assert.Equal(commitTilde.Id, commitCaret.Id);
        }
    }

    [Fact]
    public async Task GetCommitAsync_Caret0_ReturnsSameCommit()
    {
        var (repo, testRepo, hashes) = CreateLinearHistory(1);
        using (testRepo)
        {
            var commit = await repo.GetCommitAsync("HEAD^0");

            Assert.Equal(hashes[0], commit.Id.ToString());
        }
    }

    // ── Chained suffixes ─────────────────────────────────────────────────

    [Fact]
    public async Task GetCommitAsync_ChainedTilde_WalksMultipleSteps()
    {
        var (repo, testRepo, hashes) = CreateLinearHistory(3);
        using (testRepo)
        {
            // HEAD~1~1 should be the same as HEAD~2
            var chained = await repo.GetCommitAsync("HEAD~1~1");
            var direct = await repo.GetCommitAsync("HEAD~2");

            Assert.Equal(direct.Id, chained.Id);
        }
    }

    // ── Error paths ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetCommitAsync_TildePastRootCommit_Throws()
    {
        var (repo, testRepo, _) = CreateLinearHistory(0);
        using (testRepo)
        {
            // The initial commit has no parent, so ~1 from it must throw.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.GetCommitAsync("HEAD~1"));
        }
    }

    [Fact]
    public async Task GetCommitAsync_CaretOnNonExistentParent_Throws()
    {
        var (repo, testRepo, _) = CreateLinearHistory(1);
        using (testRepo)
        {
            // A normal commit has exactly one parent; selecting parent 2 must throw.
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repo.GetCommitAsync("HEAD^2"));
        }
    }
}
