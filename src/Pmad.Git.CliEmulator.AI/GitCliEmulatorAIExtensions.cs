using Microsoft.Extensions.AI;
using Pmad.Git.CliEmulator;

namespace Pmad.Git.CliEmulator.AI;

/// <summary>
/// Extension methods on <see cref="IGitCliEmulator"/> for creating MEAI tool integrations.
/// </summary>
public static class GitCliEmulatorAIExtensions
{
    /// <summary>
    /// Creates an <see cref="AIFunction"/> named <c>git</c> that wraps this emulator.
    /// The function accepts an array of arguments (e.g. <c>["log", "--oneline", "-10"]</c>).
    /// </summary>
    /// <param name="emulator">The CLI emulator to wrap.</param>
    /// <param name="userApproval">
    /// Optional approval gate.  When <see langword="null"/> all operations are auto-approved.
    /// </param>
    /// <returns>An <see cref="AIFunction"/> ready to register in <see cref="ChatOptions.Tools"/>.</returns>
    public static AIFunction CreateAIFunction(
        this IGitCliEmulator emulator,
        IUserApproval? userApproval = null)
        => GitAIFunctionFactory.Create(emulator, userApproval);

    /// <summary>
    /// Creates an <see cref="AIFunction"/> named <c>git</c> that wraps this emulator.
    /// The function accepts a single space-separated command string
    /// (e.g. <c>"commit -m 'Fix bug'"</c>) — useful for models that prefer a single string parameter.
    /// </summary>
    /// <param name="emulator">The CLI emulator to wrap.</param>
    /// <param name="userApproval">
    /// Optional approval gate.  When <see langword="null"/> all operations are auto-approved.
    /// </param>
    /// <returns>An <see cref="AIFunction"/> ready to register in <see cref="ChatOptions.Tools"/>.</returns>
    public static AIFunction CreateAIFunctionFromString(
        this IGitCliEmulator emulator,
        IUserApproval? userApproval = null)
        => GitAIFunctionFactory.CreateFromString(emulator, userApproval);
}
