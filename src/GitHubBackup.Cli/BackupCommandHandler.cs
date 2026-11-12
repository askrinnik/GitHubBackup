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
/// With <c>--silent</c>, discovered repositories are decided by <see cref="AutomaticDiscoveryDecisionHandler"/>;
/// otherwise <see cref="ConsoleDiscoveryDecisionHandler"/> asks in the console, which needs a console whose input is
/// not redirected. With <c>--dry-run</c> nothing is decided, so nobody is asked and redirected input is fine.
/// </remarks>
/// <param name="orchestrator">Runs the backup.</param>
/// <param name="automaticHandler">Decides about new and renamed repositories without asking.</param>
/// <param name="consoleHandler">Asks about new and renamed repositories in the console.</param>
/// <param name="console">The outputs of the console application.</param>
/// <param name="renderer">Shows the summary.</param>
/// <param name="environment">The host environment whose content root holds the log folder.</param>
/// <param name="timeProvider">Supplies the local date that names the log file of the day.</param>
internal sealed class BackupCommandHandler(
    IBackupOrchestrator orchestrator,
    AutomaticDiscoveryDecisionHandler automaticHandler,
    ConsoleDiscoveryDecisionHandler consoleHandler,
    CliConsole console,
    BackupSummaryRenderer renderer,
    IHostEnvironment environment,
    TimeProvider timeProvider)
{
    /// <summary>The error that stops an interactive run whose input is redirected.</summary>
    public const string RedirectedInputMessage =
        "Input is redirected, so the backup cannot ask questions. Run it again with --silent to decide automatically.";

    /// <summary>
    /// Runs the backup and shows its summary.
    /// </summary>
    /// <remarks>
    /// Without <see cref="BackupCommandOptions.Silent"/> or <see cref="BackupCommandOptions.DryRun"/> and with
    /// redirected input the run stops before the orchestrator, before discovery or any write, because no one can
    /// answer.
    /// </remarks>
    /// <param name="options">The options of the command.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The exit code of the run.</returns>
    public async Task<ExitCode> RunAsync(BackupCommandOptions options, CancellationToken cancellationToken)
    {
        IDiscoveryDecisionHandler decisionHandler = options.Silent ? automaticHandler : consoleHandler;
        var result = !options.Silent && !options.DryRun && console.IsInputRedirected
            ? BackupRunResult.Stopped([RedirectedInputMessage], [])
            : await orchestrator.RunAsync(
                new(options.Filter, decisionHandler)
                {
                    DryRun = options.DryRun,
                    Mode = options.Silent ? RunMode.Silent : RunMode.Interactive,
                },
                cancellationToken);
        await renderer.RenderAsync(result, CliLogPath.Get(environment, timeProvider));
        return result.ExitCode;
    }
}
