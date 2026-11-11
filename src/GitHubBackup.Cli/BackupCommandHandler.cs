using GitHubBackup.Core;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.Runs;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.Cli;

/// <summary>
/// Runs the <c>backup</c> command: runs the backup of every repository, shows the summary and returns the exit code.
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
    /// <summary>
    /// Runs the backup and shows its summary.
    /// </summary>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The exit code of the run.</returns>
    public async Task<ExitCode> RunAsync(CancellationToken cancellationToken)
    {
        var result = await orchestrator.RunAsync(new(BackupRunFilter.All, decisionHandler), cancellationToken);
        await renderer.RenderAsync(result, GetLogPath());
        return result.ExitCode;
    }

    /// <summary>
    /// Returns the path of the text log of the current day.
    /// </summary>
    /// <returns>The absolute path of the log file.</returns>
    private string GetLogPath() => Path.Combine(
        environment.ContentRootPath,
        LogFiles.FolderName,
        LogFiles.GetFileName(DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime), LogFiles.TextExtension));
}
