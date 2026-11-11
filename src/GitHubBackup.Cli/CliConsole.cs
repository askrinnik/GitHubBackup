using Spectre.Console;

namespace GitHubBackup.Cli;

/// <summary>
/// The two outputs of the console application.
/// </summary>
/// <param name="Output">The console that shows results, normally standard output.</param>
/// <param name="Error">The writer that receives one-line errors, normally <see cref="Console.Error"/>.</param>
internal sealed record CliConsole(IAnsiConsole Output, TextWriter Error);
