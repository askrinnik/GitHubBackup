using System.CommandLine;

namespace GitHubBackup.Cli;

/// <summary>
/// The parsed command line of the console application.
/// </summary>
/// <param name="Command">The command to run.</param>
/// <param name="Errors">The errors in the command line, one sentence each; empty when it is valid.</param>
/// <param name="ParseResult">The result of the parser, which answers <see cref="CliCommand.Information"/>.</param>
internal sealed record CliArguments(CliCommand Command, IReadOnlyList<string> Errors, ParseResult ParseResult);
