using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Discovery;
using GitHubBackup.App.Hosting;
using GitHubBackup.App.Runs;
using GitHubBackup.App.Shell;
using GitHubBackup.App.Tests.Runs;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.Infrastructure.Logging;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace GitHubBackup.App.Tests.Hosting;

/// <summary>
/// Tests <see cref="AppHost"/>: the shared host builder with the services of the WPF application added.
/// </summary>
public sealed partial class AppHostTests : IDisposable
{
    private readonly TemporaryContentRoot _contentRoot = new(AppTestSettings.ValidAppSettings);

    [Fact]
    public void BuildHost_ValidConfiguration_ResolvesMainWindowViewModel()
    {
        using var host = BuildHost();

        var viewModel = host.Services.GetRequiredService<MainWindowViewModel>();

        viewModel.CurrentViewModel.ShouldBeOfType<RepositoriesViewModel>();
        host.Services.GetRequiredService<MainWindowViewModel>().ShouldBeSameAs(viewModel);
    }

    [Fact]
    public void BuildHost_ValidConfiguration_ReplacesConsoleLifetime()
    {
        using var host = BuildHost();

        host.Services.GetRequiredService<IHostLifetime>().ShouldBeOfType<DesktopHostLifetime>();
        host.Services.GetServices<IHostLifetime>().ShouldHaveSingleItem();
    }

    [Fact]
    public void BuildHost_ValidConfiguration_UsesSharedCoreServices()
    {
        using var host = BuildHost();

        host.Services.GetRequiredService<IOptions<BackupOptions>>().Value.ShortHashLength.ShouldBe(10);
    }

    [Fact]
    public void BuildHost_RepositoriesViewModel_IsTransient()
    {
        using var host = BuildHost();

        var first = host.Services.GetRequiredService<RepositoriesViewModel>();
        var second = host.Services.GetRequiredService<RepositoriesViewModel>();

        second.ShouldNotBeSameAs(first);
    }

    [Fact]
    public void BuildHost_SettingsViewModel_IsTransient()
    {
        using var host = BuildHost();

        var first = host.Services.GetRequiredService<SettingsViewModel>();
        var second = host.Services.GetRequiredService<SettingsViewModel>();

        second.ShouldNotBeSameAs(first);
    }

    [Fact]
    public void BuildHost_HistoryViewModel_IsTransientWithBothTabs()
    {
        using var host = BuildHost();

        var first = host.Services.GetRequiredService<HistoryViewModel>();
        var second = host.Services.GetRequiredService<HistoryViewModel>();

        second.ShouldNotBeSameAs(first);
        second.Runs.ShouldNotBeSameAs(first.Runs);
        second.Archives.ShouldNotBeSameAs(first.Archives);
    }

    [Fact]
    public void BuildHost_ExplorerLauncher_IsResolved()
    {
        using var host = BuildHost();

        host.Services.GetRequiredService<IExplorerLauncher>().ShouldBeOfType<ExplorerLauncher>();
    }

    [Fact]
    public async Task BuildHost_NoRun_MainWindowCanClose()
    {
        using var host = BuildHost();

        var canClose = await host.Services.GetRequiredService<MainWindowViewModel>().CanCloseAsync();

        canClose.ShouldBeTrue();
    }

    [Fact]
    public void BuildHost_DiscoveryDecisionHandlerFactory_CreatesDialogHandler()
    {
        using var host = BuildHost();

        var factory = host.Services.GetRequiredService<IDiscoveryDecisionHandlerFactory>();

        factory.ShouldBeOfType<DialogDiscoveryDecisionHandlerFactory>();
        factory.Create(() => { }).ShouldBeOfType<DialogDiscoveryDecisionHandler>();
    }

    [Fact]
    public void BuildHost_DiscoveryDialogService_IsWpfService()
    {
        using var host = BuildHost();

        host.Services.GetRequiredService<IDiscoveryDialogService>().ShouldBeOfType<WpfDiscoveryDialogService>();
    }

    [Fact]
    public void BuildHost_BackupRunService_IsResolved()
    {
        using var host = BuildHost();

        host.Services.GetRequiredService<IBackupRunService>().ShouldBeOfType<BackupRunService>();
    }

    [Fact]
    public void BuildHost_UiDispatcher_IsWpfDispatcher()
    {
        using var host = BuildHost();

        host.Services.GetRequiredService<IUiDispatcher>().ShouldBeOfType<WpfUiDispatcher>();
    }

    [Fact]
    public void BuildHost_LiveLogFeed_IsSingleton()
    {
        using var host = BuildHost();

        var feed = host.Services.GetRequiredService<ILiveLogFeed>();

        host.Services.GetRequiredService<ILiveLogFeed>().ShouldBeSameAs(feed);
    }

    [Fact]
    public void BuildHost_RunLog_IsSingletonSharedByRepositoryScreens()
    {
        using var host = BuildHost();

        var runLog = host.Services.GetRequiredService<RunLogViewModel>();

        host.Services.GetRequiredService<RunLogViewModel>().ShouldBeSameAs(runLog);
        host.Services.GetRequiredService<RepositoriesViewModel>().RunLog.ShouldBeSameAs(runLog);
        host.Services.GetRequiredService<RepositoriesViewModel>().RunLog.ShouldBeSameAs(runLog);
    }

    [Fact]
    public void BuildHost_LiveLogBuffer_KeepsAsManyEntriesAsRunLog()
    {
        using var host = BuildHost();

        host.Services.GetRequiredService<IOptions<LiveLogOptions>>().Value.BufferCapacity.ShouldBe(RunLogViewModel.MaxEntries);
    }

    [Fact]
    public void BuildHost_LogEndpoints_AttachLiveLogAndNoConsole()
    {
        using var host = BuildHost();
        var consoleLogTarget = typeof(LiveLogOptions).Assembly.GetType("GitHubBackup.Infrastructure.Logging.ConsoleLogTarget");

        host.Services.GetRequiredService<IOptions<LiveLogOptions>>().Value.Enabled.ShouldBeTrue();
        consoleLogTarget.ShouldNotBeNull();
        host.Services.GetRequiredService<IServiceProviderIsService>().IsService(consoleLogTarget).ShouldBeFalse();
    }

    [Fact]
    public void BuildHost_EntryWrittenBeforeRunLogExists_IsShown()
    {
        var dispatcher = new ManualUiDispatcher();
        using var host = BuildHost(dispatcher);
        WriteWarning(host.Services.GetRequiredService<ILogger<AppHostTests>>(), "Written before the window");

        var runLog = host.Services.GetRequiredService<RunLogViewModel>();
        dispatcher.RunAll();

        runLog.Entries.Select(entry => entry.Message).ShouldBe(["Written before the window"]);
    }

    [Fact]
    public void Dispose_Host_StopsDeliveringEntriesToRunLog()
    {
        var dispatcher = new ManualUiDispatcher();
        var host = BuildHost(dispatcher);
        var runLog = host.Services.GetRequiredService<RunLogViewModel>();
        var sink = (ILogEventSink)host.Services.GetRequiredService<ILiveLogFeed>();

        host.Dispose();
        sink.Emit(new(DateTimeOffset.UnixEpoch, LogEventLevel.Warning, null, new MessageTemplateParser().Parse("After the exit"), []));
        dispatcher.RunAll();

        runLog.Entries.ShouldBeEmpty();
    }

    /// <summary>
    /// Deletes the content root.
    /// </summary>
    public void Dispose() => _contentRoot.Dispose();

    /// <summary>
    /// Builds the host of the WPF application over the content root.
    /// </summary>
    /// <returns>The host, not yet started; the caller disposes it.</returns>
    private IHost BuildHost() => AppHost.BuildHost(AppTestSettings.Create(_contentRoot.FolderPath));

    /// <summary>
    /// Logs <paramref name="text"/> at <see cref="LogLevel.Warning"/>.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="text">The text of the entry.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "{Text}")]
    private static partial void WriteWarning(ILogger logger, string text);

    /// <summary>
    /// Builds the host of the WPF application over the content root with <paramref name="dispatcher"/> as its UI
    /// dispatcher, so the test can run what the live log posts to the UI thread.
    /// </summary>
    /// <param name="dispatcher">The dispatcher the test runs by hand.</param>
    /// <returns>The host, not yet started; the caller disposes it.</returns>
    private IHost BuildHost(ManualUiDispatcher dispatcher)
    {
        var builder = GitHubBackupHostBuilder.Create(AppTestSettings.Create(_contentRoot.FolderPath));
        builder.Services.AddGitHubBackupApp();
        builder.Services.Replace(ServiceDescriptor.Singleton<IUiDispatcher>(dispatcher));
        return builder.Build();
    }
}
