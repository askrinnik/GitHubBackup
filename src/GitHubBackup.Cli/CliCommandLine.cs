using System.CommandLine;
using System.CommandLine.Parsing;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.Runs;

namespace GitHubBackup.Cli;

/// <summary>
/// Describes the commands and options of the console application and parses the command line.
/// </summary>
/// <remarks>
/// Configuration keys in the form <c>--Section:Key=value</c> are not options of any command: the parser leaves them
/// unmatched and accepts them, and the host reads them as configuration. Every other unmatched token is an error.
/// Response files (<c>@file</c>) are not expanded, so an argument that starts with <c>@</c> is never read as a path.
/// Error messages never quote an option value.
/// </remarks>
internal static class CliCommandLine
{
    /// <summary>The name of the backup command.</summary>
    public const string BackupCommandName = "backup";

    /// <summary>The name of the validate command.</summary>
    public const string ValidateCommandName = "validate";

    /// <summary>The name of the set-token command.</summary>
    public const string SetTokenCommandName = "set-token";

    /// <summary>The error for an argument of <c>set-token</c>, which would be the token itself.</summary>
    public const string SetTokenArgumentMessage =
        "The set-token command does not accept arguments; the token is never taken from the command line.";

    /// <summary>The error for <c>--account</c> and <c>--repo</c> given together.</summary>
    public const string AccountAndRepositoryMessage = "The --account and --repo options cannot be used together.";

    /// <summary>The error for an <c>--account</c> value that is neither an account URL nor a login.</summary>
    public const string AccountMessage = "The --account option must be https://github.com/<login> or a login.";

    /// <summary>The error for a <c>--repo</c> value that is neither a repository URL nor <c>owner/repo</c>.</summary>
    public const string RepositoryMessage = "The --repo option must be https://github.com/<owner>/<repo> or owner/repo.";

    /// <summary>The error for a <c>--config</c> value that is empty or looks like an option.</summary>
    public const string ConfigMissingMessage = "The --config option requires the path of the backup configuration file.";

    /// <summary>The error for a <c>--config</c> value that is not a path.</summary>
    public const string ConfigInvalidMessage = "The --config option is not a valid path.";

    /// <summary>
    /// Unmatched tokens longer than this are not quoted in error messages, because a long token may be a secret typed
    /// in the wrong place.
    /// </summary>
    private const int _maxQuotedTokenLength = 32;

    /// <summary>
    /// Parses <paramref name="args"/>.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The command, its options and the errors.</returns>
    public static CliArguments Parse(IReadOnlyList<string> args)
    {
        var account = new Option<string>("--account")
        {
            Description = "Back up only the repositories of this tracked account.",
            HelpName = "url|login",
        };
        account.Validators.Add(result => ValidateValue(result, IsAccountReference, AccountMessage));

        var repository = new Option<string>("--repo")
        {
            Description = "Back up only this repository of backup-config.json.",
            HelpName = "url|owner/repo",
        };
        repository.Validators.Add(result => ValidateValue(result, IsRepositoryReference, RepositoryMessage));

        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Report what the run would do without changing anything.",
        };
        var silent = new Option<bool>("--silent")
        {
            Description = "Never ask a question; decide automatically.",
        };
        var backupConfig = CreateConfigOption();

        var backup = new Command(BackupCommandName, "Backs up the repositories listed in backup-config.json.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        backup.Options.Add(account);
        backup.Options.Add(repository);
        backup.Options.Add(dryRun);
        backup.Options.Add(silent);
        backup.Options.Add(backupConfig);
        backup.Validators.Add(result =>
        {
            if (result.GetResult(account) is not null && result.GetResult(repository) is not null)
            {
                result.AddError(AccountAndRepositoryMessage);
            }
        });

        var validateConfig = CreateConfigOption();
        var validate = new Command(
            ValidateCommandName,
            "Checks backup-config.json, git, git-lfs, 7-Zip and the token source without changing anything.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        validate.Options.Add(validateConfig);

        var setToken = new Command(
            SetTokenCommandName,
            "Stores the GitHub token; the token is read from hidden input, never from an argument.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };

        var root = new RootCommand("Keeps local clones of GitHub repositories and archives them when they change.")
        {
            TreatUnmatchedTokensAsErrors = false,
        };
        root.Subcommands.Add(backup);
        root.Subcommands.Add(validate);
        root.Subcommands.Add(setToken);

        // The commands get placeholder actions so the parser accepts a command line without a command; the
        // application runs the commands itself and invokes the parser only for informational options.
        root.SetAction(_ => 0);
        backup.SetAction(_ => 0);
        validate.SetAction(_ => 0);
        setToken.SetAction(_ => 0);

        var parseResult = root.Parse(args, new ParserConfiguration { ResponseFileTokenReplacer = null });
        var parsed = parseResult.CommandResult.Command;
        IReadOnlyList<string> errors =
        [
            .. parseResult.Errors.Select(error => error.Message),
            .. DescribeUnmatchedTokens(parseResult.UnmatchedTokens, parsed == setToken),
        ];

        var command = parseResult.Action != parsed.Action ? CliCommand.Information
            : parsed == backup ? CliCommand.Backup
            : parsed == validate ? CliCommand.Validate
            : parsed == setToken ? CliCommand.SetToken
            : CliCommand.None;
        var arguments = new CliArguments(command, errors, parseResult);
        if (errors.Count > 0)
        {
            return arguments;
        }

        var configValue = command switch
        {
            CliCommand.Backup => parseResult.GetValue(backupConfig),
            CliCommand.Validate => parseResult.GetValue(validateConfig),
            _ => null,
        };
        return arguments with
        {
            BackupConfigPath = configValue is null ? null : Path.GetFullPath(configValue),
            ConfigurationArguments = [.. parseResult.UnmatchedTokens.Where(IsConfigurationArgument)],
            Backup = command == CliCommand.Backup
                ? CreateBackupOptions(parseResult, account, repository, dryRun, silent)
                : BackupCommandOptions.Default,
        };
    }

    /// <summary>
    /// Returns whether <paramref name="argument"/> has the form <c>--Section:Key=value</c>.
    /// </summary>
    /// <remarks>
    /// Command options such as <c>--silent</c> or <c>--account name</c> are left out, so they never become
    /// configuration keys.
    /// </remarks>
    /// <param name="argument">One command-line argument.</param>
    /// <returns><see langword="true"/> when the argument sets a configuration key.</returns>
    public static bool IsConfigurationArgument(string argument)
    {
        if (!argument.StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        var separator = argument.IndexOf('=', StringComparison.Ordinal);
        return separator > 2 && argument.AsSpan(2, separator - 2).Contains(':');
    }

    /// <summary>
    /// Creates the <c>--config</c> option; each command that reads the configuration file declares its own instance.
    /// </summary>
    /// <returns>The option.</returns>
    private static Option<string> CreateConfigOption()
    {
        var option = new Option<string>("--config")
        {
            Description = "The path of backup-config.json; by default next to the executable.",
            HelpName = "path",
        };
        option.Validators.Add(result =>
        {
            var value = result.GetValueOrDefault<string>();
            if (value is null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(value) || value.StartsWith('-'))
            {
                result.AddError(ConfigMissingMessage);
                return;
            }

            try
            {
                Path.GetFullPath(value);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                result.AddError(ConfigInvalidMessage);
            }
        });
        return option;
    }

    /// <summary>
    /// Reads the options of the <c>backup</c> command from a parse result without errors.
    /// </summary>
    /// <param name="parseResult">The result without errors.</param>
    /// <param name="account">The <c>--account</c> option.</param>
    /// <param name="repository">The <c>--repo</c> option.</param>
    /// <param name="dryRun">The <c>--dry-run</c> option.</param>
    /// <param name="silent">The <c>--silent</c> option.</param>
    /// <returns>The options.</returns>
    private static BackupCommandOptions CreateBackupOptions(
        ParseResult parseResult, Option<string> account, Option<string> repository, Option<bool> dryRun, Option<bool> silent)
    {
        var accountValue = parseResult.GetValue(account);
        var repositoryValue = parseResult.GetValue(repository);
        var filter = BackupRunFilter.All;
        if (accountValue is not null && GitHubUrl.TryParseAccountReference(accountValue, out var login))
        {
            filter = BackupRunFilter.ForAccount(login);
        }
        else if (repositoryValue is not null
            && GitHubUrl.TryParseRepositoryReference(repositoryValue, out var owner, out var name))
        {
            filter = BackupRunFilter.ForRepository(owner, name);
        }

        return new(filter, parseResult.GetValue(dryRun), parseResult.GetValue(silent));
    }

    /// <summary>
    /// Adds an error to <paramref name="result"/> when its value fails <paramref name="isValid"/>; the value is never
    /// part of the message.
    /// </summary>
    /// <param name="result">The result of the option.</param>
    /// <param name="isValid">Tells whether a value is acceptable.</param>
    /// <param name="message">The error message.</param>
    private static void ValidateValue(OptionResult result, Func<string, bool> isValid, string message)
    {
        var value = result.GetValueOrDefault<string>();
        if (value is not null && !isValid(value))
        {
            result.AddError(message);
        }
    }

    /// <summary>
    /// Returns whether <paramref name="value"/> is an account URL or a login.
    /// </summary>
    /// <param name="value">The value of <c>--account</c>.</param>
    /// <returns><see langword="true"/> when the value is valid.</returns>
    private static bool IsAccountReference(string value) => GitHubUrl.TryParseAccountReference(value, out _);

    /// <summary>
    /// Returns whether <paramref name="value"/> is a repository URL or <c>owner/repo</c>.
    /// </summary>
    /// <param name="value">The value of <c>--repo</c>.</param>
    /// <returns><see langword="true"/> when the value is valid.</returns>
    private static bool IsRepositoryReference(string value) => GitHubUrl.TryParseRepositoryReference(value, out _, out _);

    /// <summary>
    /// Describes the tokens that are neither a command, an option value nor a configuration key.
    /// </summary>
    /// <param name="tokens">The unmatched tokens.</param>
    /// <param name="forSetToken">Whether the command is <c>set-token</c>, whose arguments are never quoted.</param>
    /// <returns>The error messages, one per distinct problem.</returns>
    private static IEnumerable<string> DescribeUnmatchedTokens(IReadOnlyList<string> tokens, bool forSetToken)
    {
        var rejected = tokens.Where(token => !IsConfigurationArgument(token)).ToList();
        foreach (var token in rejected.Where(token => token.StartsWith('-') || !forSetToken))
        {
            yield return DescribeUnmatchedToken(token);
        }

        if (forSetToken && rejected.Any(token => !token.StartsWith('-')))
        {
            yield return SetTokenArgumentMessage;
        }
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
