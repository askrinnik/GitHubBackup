namespace GitHubBackup.Core.Processes;

/// <summary>
/// Reports that a process cannot be started: the executable or the working directory does not exist, or the
/// operating system refused to start it.
/// </summary>
/// <param name="message">The message, naming the executable or folder involved.</param>
/// <param name="innerException">The exception that caused the failure, or <see langword="null"/>.</param>
public sealed class ProcessStartException(string message, Exception? innerException = null)
    : Exception(message, innerException);
