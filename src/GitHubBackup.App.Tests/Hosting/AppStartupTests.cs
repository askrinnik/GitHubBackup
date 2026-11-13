using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Hosting;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.Infrastructure.Logging;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;

namespace GitHubBackup.App.Tests.Hosting;

/// <summary>
/// Tests <see cref="AppStartup"/>: starting and stopping the host, the start and stop log entries and the dialog
/// that reports invalid configuration.
/// </summary>
public sealed class AppStartupTests : IDisposable
{
    private const int _startedEventId = 1;
    private const int _stoppedEventId = 2;
    private const int _configurationInvalidEventId = 3;

    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();
    private readonly FakeLogger<AppStartup> _logger = new();
    private TemporaryContentRoot _contentRoot = new(AppTestSettings.ValidAppSettings);

    [Fact]
    public async Task StartAsync_ValidConfiguration_ReturnsStartedHostAndShowsNoDialog()
    {
        using var host = await StartAsync();

        host.ShouldNotBeNull();
        host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested.ShouldBeTrue();
        _dialogs.ReceivedCalls().ShouldBeEmpty();
        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartAsync_ValidConfiguration_LogsStartWithVersionAndEnvironment()
    {
        using var host = await StartAsync();

        var record = _logger.Collector.GetSnapshot().Where(r => r.Id.Id == _startedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Information);
        record.GetStructuredStateValue("Version").ShouldBe(typeof(AppStartup).Assembly.GetName().Version?.ToString());
        record.GetStructuredStateValue("Environment").ShouldBe(Environments.Production);
        await host!.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StartAsync_ValidConfiguration_WritesStartEntryToSharedLogFiles()
    {
        var before = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var startup = new AppStartup(_dialogs, AppHost.BuildHost);

        var host = await startup.StartAsync(AppTestSettings.Create(_contentRoot.FolderPath), TestContext.Current.CancellationToken);
        await AppStartup.StopAsync(host.ShouldNotBeNull(), TestContext.Current.CancellationToken);

        var after = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().DateTime);
        var textLogName = Path.GetFileName(_contentRoot.GetLogFilePath(LogFiles.TextExtension));
        var jsonLogName = Path.GetFileName(_contentRoot.GetLogFilePath(LogFiles.JsonExtension));
        new[] { before, after }.Select(date => LogFiles.GetFileName(date, LogFiles.TextExtension)).ShouldContain(textLogName);
        new[] { before, after }.Select(date => LogFiles.GetFileName(date, LogFiles.JsonExtension)).ShouldContain(jsonLogName);
        _contentRoot.ReadTextLog().ShouldContain("GitHubBackup.App");
    }

    [Fact]
    public async Task StartAsync_MissingAppSettings_ShowsConfigurationErrorAndReturnsNull()
    {
        _contentRoot.Dispose();
        _contentRoot = new();

        var host = await StartAsync();

        host.ShouldBeNull();
        _dialogs.Received(1).ShowError(
            AppStartup.DialogTitle,
            Arg.Is<string>(message => message.StartsWith(AppStartup.ConfigurationErrorHeading + Environment.NewLine, StringComparison.Ordinal)
                && message.Contains(GitHubBackupHostBuilder.AppSettingsFileName, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task StartAsync_InvalidOption_ShowsConfigurationErrorAndReturnsNull()
    {
        var host = await StartAsync("--Backup:ShortHashLength=2");

        host.ShouldBeNull();
        _dialogs.Received(1).ShowError(
            AppStartup.DialogTitle,
            string.Join(Environment.NewLine, AppStartup.ConfigurationErrorHeading, "Backup:ShortHashLength must be between 4 and 40."));
    }

    [Fact]
    public async Task StartAsync_InvalidOption_LogsConfigurationErrors()
    {
        await StartAsync("--Backup:ShortHashLength=2");

        var record = _logger.Collector.GetSnapshot().Where(r => r.Id.Id == _configurationInvalidEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Error);
        record.GetStructuredStateValue("Errors").ShouldBe("Backup:ShortHashLength must be between 4 and 40.");
        _logger.Collector.GetSnapshot().ShouldNotContain(r => r.Id.Id == _startedEventId);
    }

    [Fact]
    public async Task StopAsync_StartedHost_WritesStopEntry()
    {
        var host = await StartAsync();

        await AppStartup.StopAsync(host.ShouldNotBeNull(), TestContext.Current.CancellationToken);

        var record = _logger.Collector.GetSnapshot().Where(r => r.Id.Id == _stoppedEventId).ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Information);
    }

    /// <summary>
    /// Deletes the content root.
    /// </summary>
    public void Dispose() => _contentRoot.Dispose();

    /// <summary>
    /// Starts the host of the WPF application over the content root, with the start-up logger replaced by
    /// <see cref="_logger"/>.
    /// </summary>
    /// <param name="configurationArguments">The configuration keys, each in the form <c>--Section:Key=value</c>.</param>
    /// <returns>The started host, or <see langword="null"/> when the configuration is invalid.</returns>
    private Task<IHost?> StartAsync(params string[] configurationArguments) =>
        new AppStartup(_dialogs, BuildHostWithFakeLogger).StartAsync(
            AppTestSettings.Create(_contentRoot.FolderPath, configurationArguments),
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Builds the host of the WPF application with <see cref="_logger"/> as the logger of <see cref="AppStartup"/>.
    /// </summary>
    /// <param name="settings">The process the host is built for.</param>
    /// <returns>The host, not yet started.</returns>
    private IHost BuildHostWithFakeLogger(GitHubBackupHostSettings settings)
    {
        var builder = GitHubBackupHostBuilder.Create(settings);
        builder.Services.AddGitHubBackupApp();
        builder.Services.Replace(ServiceDescriptor.Singleton<ILogger<AppStartup>>(_logger));
        return builder.Build();
    }
}
