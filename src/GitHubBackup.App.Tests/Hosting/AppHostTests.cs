using GitHubBackup.App.Hosting;
using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.Configuration;
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

        viewModel.CurrentViewModel.ShouldBeOfType<HomeViewModel>();
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
    public void BuildHost_HomeViewModel_IsTransient()
    {
        using var host = BuildHost();

        var first = host.Services.GetRequiredService<HomeViewModel>();
        var second = host.Services.GetRequiredService<HomeViewModel>();

        second.ShouldNotBeSameAs(first);
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
