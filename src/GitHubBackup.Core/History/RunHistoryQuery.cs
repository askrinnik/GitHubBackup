using GitHubBackup.Core.Runs;

namespace GitHubBackup.Core.History;

/// <summary>
/// Selects runs of the run history; a property left <see langword="null"/> does not filter.
/// </summary>
public sealed record RunHistoryQuery
{
    /// <summary>Gets a query that selects every run.</summary>
    public static RunHistoryQuery All { get; } = new();

    /// <summary>Gets the earliest instant a selected run started at, inclusive.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Gets the instant every selected run started before, exclusive.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Gets how a selected run was started.</summary>
    public RunMode? Mode { get; init; }

    /// <summary>Gets whether a selected run was a dry run.</summary>
    public bool? IsDryRun { get; init; }

    /// <summary>Gets the exit code a selected run finished with.</summary>
    public ExitCode? ExitCode { get; init; }
}
