namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Holds what a finished run of the console executable produced.
/// </summary>
/// <param name="ExitCode">The exit code of the process.</param>
/// <param name="StandardOutput">Everything the process wrote to standard output.</param>
/// <param name="StandardError">Everything the process wrote to standard error.</param>
internal sealed record CliProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    /// <summary>
    /// Returns the non-empty lines of standard error.
    /// </summary>
    /// <returns>The lines, without line breaks.</returns>
    public string[] ErrorLines() => StandardError.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    /// <inheritdoc />
    public override string ToString() =>
        $"Exit code {ExitCode}{Environment.NewLine}stdout:{Environment.NewLine}{StandardOutput}{Environment.NewLine}stderr:{Environment.NewLine}{StandardError}";
}
