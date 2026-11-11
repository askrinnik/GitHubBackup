using System.Globalization;
using GitHubBackup.Core.Runs;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Tests <see cref="CliCommandLine"/>.
/// </summary>
public sealed class CliCommandLineTests
{
    private const string _token = "ghp_TestTokenValue0123456789abcdefABCDEF";

    [Theory]
    [InlineData(nameof(CliCommand.Backup), "backup")]
    [InlineData(nameof(CliCommand.Validate), "validate")]
    [InlineData(nameof(CliCommand.SetToken), "set-token")]
    [InlineData(nameof(CliCommand.None))]
    [InlineData(nameof(CliCommand.Information), "--help")]
    [InlineData(nameof(CliCommand.Information), "--version")]
    [InlineData(nameof(CliCommand.Information), "backup", "--help")]
    public void Parse_Command_SelectsCommand(string expected, params string[] args)
    {
        var arguments = CliCommandLine.Parse(args);

        arguments.Errors.ShouldBeEmpty();
        arguments.Command.ToString().ShouldBe(expected);
    }

    [Fact]
    public void Parse_BackupWithoutOptions_ReturnsDefaultOptions()
    {
        var arguments = CliCommandLine.Parse(["backup"]);

        arguments.Backup.ShouldBe(BackupCommandOptions.Default);
        arguments.BackupConfigPath.ShouldBeNull();
        arguments.ConfigurationArguments.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("https://github.com/octocat", "octocat")]
    [InlineData("octocat", "octocat")]
    public void Parse_Account_ReturnsAccountFilter(string value, string login)
    {
        var arguments = CliCommandLine.Parse(["backup", "--account", value]);

        arguments.Errors.ShouldBeEmpty();
        arguments.Backup.Filter.ShouldBe(BackupRunFilter.ForAccount(login));
    }

    [Theory]
    [InlineData("https://github.com/octocat/hello", "octocat", "hello")]
    [InlineData("octocat/hello", "octocat", "hello")]
    public void Parse_Repo_ReturnsRepositoryFilter(string value, string owner, string name)
    {
        var arguments = CliCommandLine.Parse(["backup", "--repo", value]);

        arguments.Errors.ShouldBeEmpty();
        arguments.Backup.Filter.ShouldBe(BackupRunFilter.ForRepository(owner, name));
    }

    [Fact]
    public void Parse_DryRunAndSilent_SetFlags()
    {
        var arguments = CliCommandLine.Parse(["backup", "--dry-run", "--silent"]);

        arguments.Errors.ShouldBeEmpty();
        arguments.Backup.ShouldBe(new BackupCommandOptions(BackupRunFilter.All, DryRun: true, Silent: true));
    }

    [Theory]
    [InlineData("--account", "octocat", "--repo", "octocat/hello")]
    [InlineData("--repo", "octocat/hello", "--account", "octocat")]
    public void Parse_AccountWithRepo_ReturnsOneError(params string[] options)
    {
        var arguments = CliCommandLine.Parse(["backup", .. options]);

        arguments.Errors.ShouldBe([CliCommandLine.AccountAndRepositoryMessage]);
    }

    [Theory]
    [InlineData("--account", "not/valid", CliCommandLine.AccountMessage)]
    [InlineData("--account", "http://github.com/someone-secret", CliCommandLine.AccountMessage)]
    [InlineData("--repo", "someone-secret", CliCommandLine.RepositoryMessage)]
    [InlineData("--repo", "https://github.com/someone-secret", CliCommandLine.RepositoryMessage)]
    public void Parse_InvalidValue_ReturnsErrorWithoutValue(string option, string value, string message)
    {
        var arguments = CliCommandLine.Parse(["backup", option, value]);

        arguments.Errors.ShouldBe([message]);
        arguments.Errors.ShouldAllBe(error => !error.Contains("secret", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("--account")]
    [InlineData("--repo")]
    [InlineData("--config")]
    public void Parse_OptionWithoutValue_ReturnsError(string option)
    {
        var arguments = CliCommandLine.Parse(["backup", option]);

        arguments.Errors.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("backup", "--config", @"D:\configs\mine.json")]
    [InlineData("backup", @"--config=D:\configs\mine.json")]
    [InlineData("validate", "--config", @"D:\configs\mine.json")]
    public void Parse_Config_ReturnsFullPath(params string[] args)
    {
        var arguments = CliCommandLine.Parse(args);

        arguments.Errors.ShouldBeEmpty();
        arguments.BackupConfigPath.ShouldBe(@"D:\configs\mine.json");
    }

    [Fact]
    public void Parse_ConfigWithoutCommand_ReturnsError()
    {
        var arguments = CliCommandLine.Parse(["--config=x.json"]);

        arguments.Errors.ShouldBe(["Unrecognized option '--config'."]);
    }

    [Theory]
    [InlineData("backup", "--config", @"configs\mine.json")]
    [InlineData("validate", @"--config=configs\mine.json")]
    public void Parse_RelativeConfig_ResolvesAgainstCurrentDirectory(params string[] args)
    {
        var arguments = CliCommandLine.Parse(args);

        arguments.BackupConfigPath.ShouldBe(Path.Combine(Environment.CurrentDirectory, "configs", "mine.json"));
    }

    [Theory]
    [InlineData("backup", "--config=")]
    [InlineData("backup", "--config", " ")]
    [InlineData("backup", "--config", "--silent")]
    [InlineData("validate", "--config", "--silent")]
    public void Parse_ConfigWithoutPath_ReturnsError(params string[] args)
    {
        var arguments = CliCommandLine.Parse(args);

        arguments.Errors.ShouldNotBeEmpty();
        arguments.Errors.ShouldAllBe(error => !error.Contains("--silent", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("backup")]
    [InlineData("validate")]
    public void Parse_ConfigNotAPath_ReturnsError(string command)
    {
        var arguments = CliCommandLine.Parse([command, "--config", "D:\\mine\0.json"]);

        arguments.Errors.ShouldBe([CliCommandLine.ConfigInvalidMessage]);
    }

    [Theory]
    [InlineData("backup")]
    [InlineData("validate")]
    public void Parse_RepeatedConfig_ReturnsError(string command)
    {
        var arguments = CliCommandLine.Parse([command, "--config", @"D:\a.json", "--config", @"D:\b.json"]);

        arguments.Errors.ShouldNotBeEmpty();
        arguments.Errors.ShouldAllBe(error => !error.Contains(".json", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("backup", "--silent", "--GitHub:Token=abc", "--Backup:ShortHashLength=12")]
    [InlineData("--GitHub:Token=abc", "backup", "--silent", "--Backup:ShortHashLength=12")]
    [InlineData("backup", "--GitHub:Token=abc", "--account", "octocat", "--Backup:ShortHashLength=12")]
    public void Parse_ConfigurationKeys_AreCollectedAndOptionsAreNot(params string[] args)
    {
        var arguments = CliCommandLine.Parse(args);

        arguments.Errors.ShouldBeEmpty();
        arguments.ConfigurationArguments.ShouldBe(["--GitHub:Token=abc", "--Backup:ShortHashLength=12"], ignoreOrder: true);
    }

    [Theory]
    [InlineData("validate", "--GitHub:Token=abc")]
    [InlineData("set-token", "--GitHub:Token=abc")]
    public void Parse_ConfigurationKeyOfAnyCommand_IsAccepted(params string[] args)
    {
        var arguments = CliCommandLine.Parse(args);

        arguments.Errors.ShouldBeEmpty();
        arguments.ConfigurationArguments.ShouldBe(["--GitHub:Token=abc"]);
    }

    [Theory]
    [InlineData("Unrecognized command or argument 'bakup'.", "bakup")]
    [InlineData("Unrecognized option '--bogus'.", "backup", "--bogus")]
    [InlineData("Unrecognized option '--account'.", "validate", "--account", "octocat")]
    [InlineData("Unrecognized option '--silent'.", "set-token", "--silent")]
    [InlineData("Unrecognized option '--dry-run'.", "validate", "--dry-run")]
    public void Parse_UnknownTokens_ReturnsErrors(string firstError, params string[] args)
    {
        var arguments = CliCommandLine.Parse(args);

        arguments.Errors.ShouldContain(firstError);
    }

    [Theory]
    [InlineData(_token)]
    [InlineData("x")]
    public void Parse_SetTokenWithArgument_ReturnsOneErrorWithoutIt(string argument)
    {
        var arguments = CliCommandLine.Parse(["set-token", argument]);

        arguments.Errors.ShouldBe([CliCommandLine.SetTokenArgumentMessage]);
    }

    [Fact]
    public void Parse_LongUnknownArgument_IsNotQuoted()
    {
        var arguments = CliCommandLine.Parse(["backup", _token]);

        var error = arguments.Errors.ShouldHaveSingleItem();
        error.ShouldNotContain(_token);
    }

    [Fact]
    public void Parse_InvalidAccountValue_NeverQuotesTokenTypedAsValue()
    {
        var arguments = CliCommandLine.Parse(["backup", "--account", _token + "/x"]);

        arguments.Errors.ShouldBe([CliCommandLine.AccountMessage]);
    }

    [Theory]
    [InlineData("--dry-run=" + _token)]
    [InlineData("--silent=" + _token)]
    public void Parse_TokenGivenToFlagOption_ReturnsErrorWithoutIt(string argument)
    {
        var arguments = CliCommandLine.Parse(["backup", argument]);

        var error = arguments.Errors.ShouldHaveSingleItem();
        error.ShouldNotContain(_token);
    }

    [Fact]
    public void Parse_UnderRussianCulture_ReturnsEnglishMessages()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = new CultureInfo("ru-RU");
        try
        {
            var arguments = CliCommandLine.Parse(["backup", "--config"]);

            arguments.Errors.ShouldHaveSingleItem().ShouldBe("Required argument missing for option: '--config'.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
