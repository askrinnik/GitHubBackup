using GitHubBackup.Core;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace GitHubBackup.Cli;

/// <summary>
/// Runs the <c>validate</c> command: checks <c>backup-config.json</c>, the external tools and the token source
/// without contacting GitHub and without changing anything.
/// </summary>
/// <remarks>
/// The three checks are independent, so one run reports every problem. Only the name of the token source is shown,
/// never the token.
/// </remarks>
/// <param name="console">The outputs of the console application.</param>
/// <param name="configStore">Reads <c>backup-config.json</c>.</param>
/// <param name="pathResolver">Resolves the folders of the repositories, which rejects names that cannot be a folder.</param>
/// <param name="toolLocator">Locates git, git-lfs and 7-Zip.</param>
/// <param name="tokenProvider">Finds the GitHub token.</param>
/// <param name="options">The <c>Backup</c> options that name the configuration file.</param>
/// <param name="environment">The host environment whose content root holds the log folder.</param>
/// <param name="timeProvider">Supplies the local date that names the log file of the day.</param>
internal sealed class ValidateCommandHandler(
    CliConsole console,
    IBackupConfigStore configStore,
    IRepositoryPathResolver pathResolver,
    IToolLocator toolLocator,
    ITokenProvider tokenProvider,
    IOptions<BackupOptions> options,
    IHostEnvironment environment,
    TimeProvider timeProvider)
{
    /// <summary>The line that reports a successful validation.</summary>
    public const string PassedMessage = "Validation passed.";

    /// <summary>
    /// Runs the checks and writes one line per result.
    /// </summary>
    /// <param name="cancellationToken">Cancels the checks.</param>
    /// <returns><see cref="ExitCode.Success"/> when every check passes, otherwise <see cref="ExitCode.Critical"/>.</returns>
    public async Task<ExitCode> RunAsync(CancellationToken cancellationToken)
    {
        List<string> errors = [];
        await CheckConfigAsync(errors, cancellationToken);
        await CheckToolsAsync(errors, cancellationToken);
        var token = await tokenProvider.GetTokenAsync(cancellationToken);
        console.Output.WriteLine($"Token: {token.SourceName}");

        if (errors.Count == 0)
        {
            console.Output.WriteLine(PassedMessage);
            return ExitCode.Success;
        }

        foreach (var error in errors)
        {
            await console.Error.WriteLineAsync(BackupSummaryRenderer.ErrorPrefix + error);
        }

        await console.Error.WriteLineAsync(BackupSummaryRenderer.LogPrefix + CliLogPath.Get(environment, timeProvider));
        return ExitCode.Critical;
    }

    /// <summary>
    /// Loads <c>backup-config.json</c> and builds the work items, which validates every repository entry.
    /// </summary>
    /// <param name="errors">Receives the errors found.</param>
    /// <param name="cancellationToken">Cancels reading the file.</param>
    /// <returns>A task that completes when the check is done.</returns>
    private async Task CheckConfigAsync(List<string> errors, CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await configStore.LoadAsync(cancellationToken);
            console.Output.WriteLine($"backup-config.json: {options.Value.ConfigPath}");
            foreach (var warning in loaded.Warnings)
            {
                console.Output.MarkupLine($"[yellow]{Markup.Escape(BackupSummaryRenderer.WarningPrefix + warning)}[/]");
            }

            BackupWorkItem.CreateAll(loaded.Config, pathResolver);
        }
        catch (BackupConfigException exception)
        {
            errors.AddRange(exception.Errors);
        }
        catch (ArgumentException exception)
        {
            errors.Add(exception.Message);
        }
    }

    /// <summary>
    /// Locates the external tools.
    /// </summary>
    /// <param name="errors">Receives the error found.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>A task that completes when the check is done.</returns>
    private async Task CheckToolsAsync(List<string> errors, CancellationToken cancellationToken)
    {
        try
        {
            var tools = await toolLocator.LocateAsync(cancellationToken);
            console.Output.WriteLine($"git: {tools.Git.Path} ({tools.Git.Version})");
            console.Output.WriteLine($"git-lfs: {tools.GitLfsVersion}");
            console.Output.WriteLine($"7-Zip: {tools.SevenZip.Path} ({tools.SevenZip.Version})");
        }
        catch (ToolUnavailableException exception)
        {
            errors.Add(exception.Message);
        }
    }
}
