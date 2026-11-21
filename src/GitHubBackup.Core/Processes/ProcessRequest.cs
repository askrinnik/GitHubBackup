using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Processes;

/// <summary>
/// Describes one run of an external tool for <see cref="IProcessRunner"/>.
/// </summary>
public sealed record ProcessRequest
{
    /// <summary>Gets the fully qualified path of the executable.</summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Gets the arguments, each passed to the process as a separate argument without a shell in between.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>Gets the fully qualified path of the folder the process runs in.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>
    /// Gets the environment variables set for this process on top of the environment it inherits.
    /// </summary>
    public IReadOnlyList<ProcessEnvironmentVariable> EnvironmentVariables { get; init; } = [];

    /// <summary>
    /// Gets a value indicating whether standard output carries a secret: its lines are returned in
    /// <see cref="ProcessResult.StandardOutput"/> but never logged.
    /// </summary>
    /// <remarks>
    /// The masker cannot hide a secret the tool prints before the caller knows its value, such as a token read from
    /// another program's store.
    /// </remarks>
    public bool IsStandardOutputSecret { get; init; }

    /// <summary>
    /// Gets a short English description of what the run does, logged before the command line so that the log reads
    /// without knowledge of the tool's options.
    /// </summary>
    /// <remarks>
    /// A constant text: it never contains a branch, a path or a URL, which belong to the detail entries only.
    /// </remarks>
    public required string Description { get; init; }

    /// <summary>Gets the level of the entry that carries <see cref="Description"/>.</summary>
    public LogLevel LogLevel { get; init; } = LogLevel.Information;

    /// <summary>
    /// Gets the level of the entries logged for the command line, every output line and a successful exit of the
    /// process.
    /// </summary>
    /// <remarks>
    /// A non-zero exit, a cancellation and a failed kill are logged as <see cref="LogLevel.Warning"/> whatever this
    /// level is.
    /// </remarks>
    public LogLevel DetailsLogLevel { get; init; } = LogLevel.Debug;
}
