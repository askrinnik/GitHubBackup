using GitHubBackup.Core;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.Runs;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.Cli;

/// <summary>
/// Runs the <c>backup</c> command: runs the backup of the selected repositories, shows the summary and returns the
/// exit code.
/// </summary>
/// <remarks>
/// Discovered repositories are decided by <see cref="AutomaticDiscoveryDecisionHandler"/>, the policy of the silent
/// mode, because the command does not ask questions in the console.
/// </remarks>
/// <param name="orchestrator">Runs the backup.</param>
/// <param name="decisionHandler">Decides about new and renamed repositories without asking.</param>
/// <param name="renderer">Shows the summary.</param>
/// <param name="environment">The host environment whose content root holds the log folder.</param>
/// <param name="timeProvider">Supplies the local date that names the log file of the day.</param>
internal sealed class BackupCommandHandler(
    IBackupOrchestrator orchestrator,
    AutomaticDiscoveryDecisionHandler decisionHandler,
    BackupSummaryRenderer renderer,
    IHostEnvironment environment,
    TimeProvider timeProvider)
{
    /// <summary>The error that stops a run with <c>--dry-run</c>, which is not available yet.</summary>
    public const string DryRunNotImplementedMessage = "The --dry-run option is not implemented yet; nothing was changed.";

    /// <summary>
    /// Runs the backup and shows its summary.
    /// </summary>
    /// <remarks>
    /// With <see cref="BackupCommandOptions.DryRun"/> the run stops before the orchestrator, so nothing is changed.
    /// </remarks>
    /// <param name="options">The options of the command.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The exit code of the run.</returns>
    public async Task<ExitCode> RunAsync(BackupCommandOptions options, CancellationToken cancellationToken)
    {
        var result = options.DryRun
            ? BackupRunResult.Stopped([DryRunNotImplementedMessage], [])
            : await orchestrator.RunAsync(new(options.Filter, decisionHandler), cancellationToken);
        await renderer.RenderAsync(result, CliLogPath.Get(environment, timeProvider));
        return result.ExitCode;
    }
}
