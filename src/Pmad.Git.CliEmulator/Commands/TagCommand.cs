using System.CommandLine;
using Pmad.Git.CliEmulator.Internal;

namespace Pmad.Git.CliEmulator.Commands;

internal static class TagCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("tag") { Description = "Create, list, or delete a tag" };
        var listOpt = new Option<bool>("-l", "--list") { Description = "List tags" };
        var deleteOpt = new Option<bool>("-d") { Description = "Delete a tag" };
        var nameArg = new Argument<string?>("name") { Description = "Tag name", Arity = ArgumentArity.ZeroOrOne };
        var commitArg = new Argument<string?>("commit") { Description = "Commit to tag (default HEAD)", Arity = ArgumentArity.ZeroOrOne };

        cmd.Options.Add(listOpt);
        cmd.Options.Add(deleteOpt);
        cmd.Arguments.Add(nameArg);
        cmd.Arguments.Add(commitArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var list = pr.GetValue(listOpt);
            var delete = pr.GetValue(deleteOpt);
            var name = pr.GetValue(nameArg);
            var commitRef = pr.GetValue(commitArg);

            try
            {
                if (delete)
                {
                    if (string.IsNullOrEmpty(name))
                    {
                        return ctx.WriteError("Tag name required.");
                    }
                    var tagRef = $"refs/tags/{name}";
                    var resolved = await ctx.Repository.ReferenceStore.TryResolveReferenceAsync(tagRef, ct);
                    if (!resolved.HasValue)
                    {
                        return ctx.WriteError($"tag '{name}' not found.");
                    }
                    await ctx.Repository.DeleteReferenceAsync(tagRef, ct);
                    await ctx.StdOut.WriteLineAsync($"Deleted tag '{name}'.");
                    return 0;
                }

                if (list || string.IsNullOrEmpty(name))
                {
                    var tags = await ctx.Repository.GetReferencesByPrefixAsync("refs/tags/", ct);
                    foreach (var tag in tags.Keys.OrderBy(t => t, StringComparer.Ordinal))
                    {
                        await ctx.StdOut.WriteLineAsync(tag["refs/tags/".Length..]);
                    }
                    return 0;
                }

                var commit = await ctx.Repository.GetCommitAsync(commitRef, ct);
                await ctx.Repository.CreateReferenceAsync($"refs/tags/{name}", commit.Id, overwrite: false, ct);
                await ctx.StdOut.WriteLineAsync($"Tag '{name}' created at {commit.Id.ToString()[..7]}.");
                return 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not GitCliDeniedException)
            {
                return ctx.WriteError(ex.Message);
            }
        });
        return cmd;
    }
}
