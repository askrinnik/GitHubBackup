using GitHubBackup.Core;

namespace GitHubBackup.Cli;

/// <summary>
/// Runs the <c>set-token</c> command, which does not store a token yet.
/// </summary>
/// <param name="console">The outputs of the console application.</param>
internal sealed class SetTokenCommandHandler(CliConsole console)
{
    /// <summary>The line written to the error output.</summary>
    public const string NotImplementedMessage = "Error: The set-token command is not implemented yet.";

    /// <summary>
    /// Reports that the command is not available.
    /// </summary>
    /// <returns><see cref="ExitCode.Critical"/>.</returns>
    public async Task<ExitCode> RunAsync()
    {
        await console.Error.WriteLineAsync(NotImplementedMessage);
        return ExitCode.Critical;
    }
}
