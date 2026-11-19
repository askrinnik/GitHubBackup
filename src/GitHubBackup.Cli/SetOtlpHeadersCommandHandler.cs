using GitHubBackup.Core;
using GitHubBackup.Core.Security;
using GitHubBackup.Core.Telemetry;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.Hosting;
using Spectre.Console;

namespace GitHubBackup.Cli;

/// <summary>
/// Runs the <c>set-otlp-headers</c> command: reads the OTLP headers with hidden input and saves them in the
/// <see cref="IOtlpHeadersStore"/>, or removes the saved headers when the input is empty.
/// </summary>
/// <remarks>
/// The command never changes <c>appsettings.json</c>; it warns when the file holds the headers in plain text.
/// </remarks>
/// <param name="console">The outputs of the console application.</param>
/// <param name="store">The store that keeps the headers.</param>
/// <param name="telemetrySettings">Tells whether the application settings file holds the headers.</param>
/// <param name="masker">The masker of the host, which learns the entered headers before they are checked.</param>
internal sealed class SetOtlpHeadersCommandHandler(
    CliConsole console, IOtlpHeadersStore store, ITelemetrySettingsStore telemetrySettings, ISecretMasker masker)
{
    /// <summary>The question that asks for the headers.</summary>
    public const string PromptText = "OTLP headers (name=value pairs, empty to remove):";

    /// <summary>The hint written when standard input is redirected, so no one can type the headers.</summary>
    public const string RedirectedInputMessage =
        "Input is redirected, so set-otlp-headers cannot read the headers. Run it in an interactive console.";

    /// <summary>The prefix of the line that confirms the headers are saved; the store location follows it.</summary>
    public const string SavedMessage = "OTLP headers saved to ";

    /// <summary>The prefix of the line that confirms the headers are removed; the store location follows it.</summary>
    public const string RemovedMessage = "OTLP headers removed from ";

    /// <summary>The warning written when <c>appsettings.json</c> holds the headers.</summary>
    public const string AppSettingsHeadersMessage =
        "appsettings.json also sets OpenTelemetry:Headers, in plain text; remove the key from the file.";

    /// <summary>
    /// Asks for the headers and saves or removes them.
    /// </summary>
    /// <param name="cancellationToken">Cancels the question and the save.</param>
    /// <returns>
    /// <see cref="ExitCode.Success"/> when the headers are saved or removed; <see cref="ExitCode.Critical"/> when
    /// input is redirected, the headers are not well formed or the store cannot be written.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<ExitCode> RunAsync(CancellationToken cancellationToken)
    {
        if (console.IsInputRedirected)
        {
            await console.Error.WriteLineAsync(BackupSummaryRenderer.ErrorPrefix + RedirectedInputMessage);
            return ExitCode.Critical;
        }

        var prompt = new TextPrompt<string>(PromptText).Secret().AllowEmpty();
        var headers = (await prompt.ShowAsync(console.Output, cancellationToken)).Trim();
        HostSecretMasker.RegisterHeaders(masker, headers);

        if (headers.Length > 0 && OpenTelemetryOptionsValidator.ValidateHeadersFormat(headers) is { } formatError)
        {
            await console.Error.WriteLineAsync(BackupSummaryRenderer.ErrorPrefix + formatError);
            return ExitCode.Critical;
        }

        try
        {
            if (headers.Length == 0)
            {
                await store.DeleteAsync(cancellationToken);
            }
            else
            {
                await store.WriteAsync(headers, cancellationToken);
            }
        }
        catch (OtlpHeadersStoreException exception)
        {
            await console.Error.WriteLineAsync(BackupSummaryRenderer.ErrorPrefix + masker.Mask(exception.Message));
            return ExitCode.Critical;
        }

        console.Output.WriteLine((headers.Length == 0 ? RemovedMessage : SavedMessage) + store.Location);
        if (telemetrySettings.HeadersInAppSettings)
        {
            console.Output.MarkupLine(
                $"[yellow]{Markup.Escape(BackupSummaryRenderer.WarningPrefix + AppSettingsHeadersMessage)}[/]");
        }

        return ExitCode.Success;
    }
}
