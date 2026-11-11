using System.CommandLine;

namespace GitHubBackup.Cli;

/// <summary>
/// The parsed command line of the console application.
/// </summary>
/// <param name="Command">The command to run.</param>
/// <param name="Errors">The errors in the command line, one sentence each; empty when it is valid.</param>
/// <param name="ParseResult">The result of the parser, which answers <see cref="CliCommand.Information"/>.</param>
internal sealed record CliArguments(CliCommand Command, IReadOnlyList<string> Errors, ParseResult ParseResult)
{
    /// <summary>Gets the absolute path given by <c>--config</c>, or <see langword="null"/> when the option is absent.</summary>
    public string? BackupConfigPath { get; init; }

    /// <summary>Gets the configuration keys of the command line, each in the form <c>--Section:Key=value</c>.</summary>
    public IReadOnlyList<string> ConfigurationArguments { get; init; } = [];

    /// <summary>Gets the options of <see cref="CliCommand.Backup"/>.</summary>
    public BackupCommandOptions Backup { get; init; } = BackupCommandOptions.Default;
}
