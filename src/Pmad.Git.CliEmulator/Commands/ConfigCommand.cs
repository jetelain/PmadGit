using System.CommandLine;
using Pmad.Git.CliEmulator.Internal;

namespace Pmad.Git.CliEmulator.Commands;

internal static class ConfigCommand
{
    public static Command Build(CommandContext ctx)
    {
        var cmd = new Command("config") { Description = "Get and set repository or global options" };
        var globalOpt = new Option<bool>("--global") { Description = "Use global config" };
        var unsetOpt = new Option<bool>("--unset") { Description = "Remove a variable" };
        var getOpt = new Option<bool>("--get") { Description = "Get value for given key" };
        var keyArg = new Argument<string>("key") { Description = "Config key (e.g. user.name)" };
        var valueArg = new Argument<string?>("value") { Description = "Value to set", Arity = ArgumentArity.ZeroOrOne };

        cmd.Options.Add(globalOpt);
        cmd.Options.Add(unsetOpt);
        cmd.Options.Add(getOpt);
        cmd.Arguments.Add(keyArg);
        cmd.Arguments.Add(valueArg);

        cmd.SetAction(async (ParseResult pr, CancellationToken ct) =>
        {
            var global = pr.GetValue(globalOpt);
            var unset = pr.GetValue(unsetOpt);
            var get = pr.GetValue(getOpt);
            var key = pr.GetValue(keyArg)!;
            var value = pr.GetValue(valueArg);

            try
            {
                if (unset)
                {
                    await ctx.Repository.UnsetConfigAsync(key, global, ct);
                    return 0;
                }
                if (get)
                {
                    var val = await ctx.Repository.GetConfigAsync(key, global, ct);
                    if (val != null)
                    {
                        await ctx.StdOut.WriteLineAsync(val);
                        return 0;
                    }
                    return 1;
                }
                if (value != null)
                {
                    await ctx.Repository.SetConfigAsync(key, value, global, ct);
                    return 0;
                }
                var result = await ctx.Repository.GetConfigAsync(key, global, ct);
                if (result != null)
                {
                    await ctx.StdOut.WriteLineAsync(result);
                    return 0;
                }
                return 1;
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not GitCliDeniedException)
            {
                return ctx.WriteError(ex.Message);
            }
        });
        return cmd;
    }
}
