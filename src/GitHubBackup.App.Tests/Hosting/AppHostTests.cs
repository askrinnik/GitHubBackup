using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Discovery;
using GitHubBackup.App.Hosting;
using GitHubBackup.App.Runs;
using GitHubBackup.App.Shell;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Infrastructure.Logging;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GitHubBackup.App.Tests.Hosting;

/// <summary>
/// Tests <see cref="AppHost"/>: the shared host builder with the services of the WPF application added.
/// </summary>
public sealed class AppHostTests : IDisposable
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

    /// <summary>
    /// Deletes the content root.
    /// </summary>
    public void Dispose() => _contentRoot.Dispose();

    /// <summary>
    /// Builds the host of the WPF application over the content root.
    /// </summary>
    /// <returns>The host, not yet started; the caller disposes it.</returns>
    private IHost BuildHost() => AppHost.BuildHost(AppTestSettings.Create(_contentRoot.FolderPath));
}
