namespace GitHubBackup.Core.Tools;

/// <summary>
/// Reports that a required external tool is missing or does not work; the run cannot start without it.
/// </summary>
/// <param name="message">The message, naming the tool, its path and the configuration key involved.</param>
/// <param name="innerException">The exception that caused the failure, or <see langword="null"/>.</param>
public sealed class ToolUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
