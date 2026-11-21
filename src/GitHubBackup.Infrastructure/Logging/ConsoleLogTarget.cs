namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Marks a host that writes log entries to its console, and holds the writer that receives them.
/// </summary>
/// <param name="Writer">The writer of the console log, normally standard error.</param>
internal sealed record ConsoleLogTarget(TextWriter Writer);
