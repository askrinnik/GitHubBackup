using System.CommandLine;
using GitHubBackup.Infrastructure.Hosting;

namespace GitHubBackup.Cli;

/// <summary>
/// Describes the commands and options of the console application and parses the command line.
/// </summary>
/// <remarks>
/// Configuration keys in the form <c>--Section:Key=value</c> are not options of any command: the parser leaves them
/// unmatched and accepts them, and the host reads them as configuration. Every other unmatched token is an error.
/// Response files (<c>@file</c>) are not expanded, so an argument that starts with <c>@</c> is never read as a path.
/// </remarks>
internal static class CliCommandLine
{
    /// <summary>The name of the backup command.</summary>
    public const string BackupCommandName = "backup";

    /// <summary>
    /// Unmatched tokens longer than this are not quoted in error messages, because a long token may be a secret typed
    /// in the wrong place.
    /// </summary>
    private const int _maxQuotedTokenLength = 32;

    /// <summary>
    /// Parses <paramref name="args"/>.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The command and the errors.</returns>
    public static CliArguments Parse(IReadOnlyList<string> args)
    {
        var backup = new Command(BackupCommandName, "Backs up the repositories listed in backup-config.json.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        var root = new RootCommand("Keeps local clones of GitHub repositories and archives them when they change.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        root.Options.Add(new Option<string>(GitHubBackupHostBuilder.ConfigOption)
        {
            Description = "The path of backup-config.json; by default next to the executable.",
            HelpName = "path",
            Recursive = true,
        });
        root.Subcommands.Add(backup);

        // The commands get placeholder actions so the parser accepts a command line without a command; the
        // application runs the commands itself and invokes the parser only for informational options.
        root.SetAction(_ => 0);
        backup.SetAction(_ => 0);

        var parseResult = root.Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });
        IReadOnlyList<string> errors =
        [
            .. parseResult.Errors.Select(error => error.Message),
            .. parseResult.UnmatchedTokens
                .Where(token => !GitHubBackupHostBuilder.IsConfigurationArgument(token))
                .Select(DescribeUnmatchedToken),
        ];

        var command = parseResult.Action == backup.Action ? CliCommand.Backup
            : parseResult.Action == root.Action ? CliCommand.None
            : CliCommand.Information;
        return new(command, errors, parseResult);
    }

    /// <summary>
    /// Describes a token that is neither a command, an option nor a configuration key.
    /// </summary>
    /// <param name="token">The token.</param>
    /// <returns>The error message; an option is named without its value.</returns>
    private static string DescribeUnmatchedToken(string token)
    {
        if (token.StartsWith('-'))
        {
            var separator = token.IndexOf('=', StringComparison.Ordinal);
            return $"Unrecognized option '{(separator < 0 ? token : token[..separator])}'.";
        }

        return token.Length <= _maxQuotedTokenLength
            ? $"Unrecognized command or argument '{token}'."
            : "Unrecognized argument; it is not shown because it may hold a secret.";
    }
}
