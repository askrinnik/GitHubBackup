using GitHubBackup.Core;
using GitHubBackup.Core.Authentication;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Tools;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Spectre.Console.Testing;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Tests <see cref="ValidateCommandHandler"/> with substitute services.
/// </summary>
public sealed class ValidateCommandHandlerTests : IDisposable
{
    private const string _contentRoot = @"C:\app";
    private const string _secret = "ghp_TestTokenValue0123456789abcdefABCDEF";

    private readonly IBackupConfigStore _configStore = Substitute.For<IBackupConfigStore>();
    private readonly IRepositoryPathResolver _pathResolver = Substitute.For<IRepositoryPathResolver>();
    private readonly IToolLocator _toolLocator = Substitute.For<IToolLocator>();
    private readonly ITokenProvider _tokenProvider = Substitute.For<ITokenProvider>();
    private readonly TestConsole _output = TestCliConsole.CreateOutput();
    private readonly StringWriter _error = new();
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));

    /// <summary>
    /// Sets up valid results for the three checks and a UTC local time zone.
    /// </summary>
    public ValidateCommandHandlerTests()
    {
        _timeProvider.SetLocalTimeZone(TimeZoneInfo.Utc);
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(new BackupConfigLoadResult(new BackupConfig(), []));
        _toolLocator.LocateAsync(Arg.Any<CancellationToken>())
            .Returns(new ToolSet(new(@"C:\git\git.exe", "2.46.0"), "3.5.1", new(@"C:\7z\7z.exe", "24.08")));
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(new GitHubToken(_secret, "environment variable"));
    }

    [Fact]
    public async Task RunAsync_EverythingValid_ReturnsSuccessAndWritesOneLinePerCheck()
    {
        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Success);
        _error.ToString().ShouldBeEmpty();
        _output.Output.ReplaceLineEndings().ShouldBe(
            """
            backup-config.json: C:\app\backup-config.json
            git: C:\git\git.exe (2.46.0)
            git-lfs: 3.5.1
            7-Zip: C:\7z\7z.exe (24.08)
            Token: environment variable
            Validation passed.

            """.ReplaceLineEndings());
    }

    [Fact]
    public async Task RunAsync_Warnings_WritesThemAndReturnsSuccess()
    {
        _configStore.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(new BackupConfigLoadResult(new BackupConfig(), ["The exclusion mask 'x' matches nothing."]));

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Success);
        _output.Output.ShouldContain("Warning: The exclusion mask 'x' matches nothing.");
    }

    [Fact]
    public async Task RunAsync_InvalidBackupConfig_ReportsErrorsAndStillChecksToolsAndToken()
    {
        _configStore.LoadAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new BackupConfigException(@"C:\app\backup-config.json", ["backupRoot is required.", "schemaVersion is wrong."]));

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Critical);
        _error.ToString().ReplaceLineEndings("\n").ShouldBe(
            "Error: backupRoot is required.\nError: schemaVersion is wrong.\nLog: C:\\app\\logs\\githubbackup-20261111.log\n");
        _output.Output.ShouldContain("git: C:\\git\\git.exe (2.46.0)");
        _output.Output.ShouldContain("Token: environment variable");
        _output.Output.ShouldNotContain("Validation passed.");
    }

    [Fact]
    public async Task RunAsync_RepositoryUrlNotValid_ReportsError()
    {
        var config = new BackupConfig { Repositories = [new() { Url = "http://example.com/x/y" }] };
        _configStore.LoadAsync(Arg.Any<CancellationToken>()).Returns(new BackupConfigLoadResult(config, []));

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Critical);
        _error.ToString().ShouldContain("Error: The repository URL 'http://example.com/x/y' is not a GitHub repository URL.");
    }

    [Fact]
    public async Task RunAsync_ToolUnavailable_ReportsErrorAndReturnsCritical()
    {
        _toolLocator.LocateAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new ToolUnavailableException("7-Zip was not found."));

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Critical);
        _error.ToString().ShouldStartWith("Error: 7-Zip was not found.");
        _output.Output.ShouldContain("backup-config.json: C:\\app\\backup-config.json");
    }

    [Fact]
    public async Task RunAsync_AnonymousToken_ReportsAnonymousAndReturnsSuccess()
    {
        _tokenProvider.GetTokenAsync(Arg.Any<CancellationToken>()).Returns(GitHubToken.Anonymous);

        var exitCode = await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        exitCode.ShouldBe(ExitCode.Success);
        _output.Output.ShouldContain("Token: anonymous");
    }

    [Fact]
    public async Task RunAsync_Always_NeverWritesTokenValueOrChangesAnything()
    {
        await CreateHandler().RunAsync(TestContext.Current.CancellationToken);

        _output.Output.ShouldNotContain(_secret);
        _error.ToString().ShouldNotContain(_secret);
        await _configStore.DidNotReceiveWithAnyArgs().SaveAsync(default!, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Disposes the outputs.
    /// </summary>
    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    /// <summary>
    /// Creates the handler over the substitutes, the test outputs and the fixed clock.
    /// </summary>
    /// <returns>The handler.</returns>
    private ValidateCommandHandler CreateHandler()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_contentRoot);
        var options = Options.Create(new BackupOptions { ConfigPath = @"C:\app\backup-config.json" });
        return new(new(_output, _error, IsInputRedirected: false), _configStore, _pathResolver, _toolLocator, _tokenProvider, options, environment, _timeProvider);
    }
}
