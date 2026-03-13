
using BenchmarkDotNet.Running;

namespace Pmad.Git.LocalRepositories.Benchmark;

public class Program
{
    public static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Need to specify a git repository path as the first argument");
            return;
        }

        // Pass the repo path to worker processes via the environment
        Environment.SetEnvironmentVariable("GIT_REPO_PATH", args[0]);

        BenchmarkRunner.Run<GitRepositoryBenchmarks>(null, args.Length > 1 ? args[1..] : []);
    }
}

