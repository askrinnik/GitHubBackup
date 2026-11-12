using GitHubBackup.Core;
using GitHubBackup.Core.Authentication;
using Spectre.Console;

namespace GitHubBackup.Cli;

/// <summary>
/// Runs the <c>set-token</c> command: reads the token with hidden input and saves it in the <see cref="ITokenStore"/>.
/// </summary>
/// <param name="console">The outputs of the console application.</param>
/// <param name="store">The store that keeps the token.</param>
internal sealed class SetTokenCommandHandler(CliConsole console, ITokenStore store)
{
    /// <summary>The question that asks for the token.</summary>
    public const string PromptText = "GitHub token:";

    /// <summary>The message shown when the input is blank, before the question is asked again.</summary>
    public const string BlankTokenMessage = "The token cannot be empty.";

    /// <summary>The hint written when standard input is redirected, so no one can type the token.</summary>
    public const string RedirectedInputMessage =
        "Input is redirected, so set-token cannot read the token. Run it in an interactive console.";

    /// <summary>The prefix of the line that confirms the token is saved; the store location follows it.</summary>
    public const string SavedMessage = "Token saved to ";

    /// <summary>
    /// Asks for the token and saves it.
    /// </summary>
    /// <param name="cancellationToken">Cancels the question and the save.</param>
    /// <returns>
    /// <see cref="ExitCode.Success"/> when the token is saved; <see cref="ExitCode.Critical"/> when input is redirected
    /// or the store cannot be written.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<ExitCode> RunAsync(CancellationToken cancellationToken)
    {
        if (console.IsInputRedirected)
        {
            await console.Error.WriteLineAsync(BackupSummaryRenderer.ErrorPrefix + RedirectedInputMessage);
            return ExitCode.Critical;
        }

        var prompt = new TextPrompt<string>(PromptText)
            .Secret()
            .Validate(value => string.IsNullOrWhiteSpace(value)
                ? ValidationResult.Error(BlankTokenMessage)
                : ValidationResult.Success());
        var token = (await prompt.ShowAsync(console.Output, cancellationToken)).Trim();

        try
        {
            await store.WriteAsync(token, cancellationToken);
        }
        catch (TokenStoreException exception)
        {
            await console.Error.WriteLineAsync(BackupSummaryRenderer.ErrorPrefix + exception.Message);
            return ExitCode.Critical;
        }

        console.Output.WriteLine(SavedMessage + store.Location);
        return ExitCode.Success;
    }
}
