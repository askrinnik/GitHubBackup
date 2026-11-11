using GitHubBackup.Core.Runs;

namespace GitHubBackup.Cli;

/// <summary>
/// The options of the <c>backup</c> command.
/// </summary>
/// <param name="Filter">The repositories the run is limited to.</param>
/// <param name="DryRun">Whether the run only reports what it would do.</param>
/// <param name="Silent">Whether the run never asks a question.</param>
internal sealed record BackupCommandOptions(BackupRunFilter Filter, bool DryRun, bool Silent)
{
    /// <summary>Gets the options of <c>backup</c> without any option: every repository, with questions allowed.</summary>
    public static BackupCommandOptions Default { get; } = new(BackupRunFilter.All, DryRun: false, Silent: false);
}
