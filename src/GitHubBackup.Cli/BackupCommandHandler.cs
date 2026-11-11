using GitHubBackup.Core;
using GitHubBackup.Core.Runs;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.Cli;

/// <summary>
/// Runs the <c>backup</c> command: runs the backup, shows the summary and returns the exit code.
/// </summary>
/// <param name="orchestrator">Runs the backup.</param>
/// <param name="renderer">Shows the summary.</param>
/// <param name="environment">The host environment whose content root holds the log folder.</param>
/// <param name="timeProvider">Supplies the local date that names the log file of the day.</param>
internal sealed class BackupCommandHandler(
    IBackupOrchestrator orchestrator,
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
        var result = await orchestrator.RunAsync(cancellationToken);
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
