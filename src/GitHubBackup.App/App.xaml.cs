using System.Windows;
using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Hosting;
using GitHubBackup.App.Views;
using GitHubBackup.Core;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.App;

/// <summary>
/// Defines the WPF application: starts the host, shows <see cref="Views.MainWindow"/> from the host's services and stops
/// the host when the window closes.
/// </summary>
public sealed partial class App : Application
{
    /// <summary>The start-up code that builds, starts and stops the host.</summary>
    private readonly AppStartup _startup = new(new MessageBoxDialogService(), AppHost.BuildHost);

    /// <summary>The started host, or <see langword="null"/> before it has started.</summary>
    private IHost? _host;

    /// <summary>
    /// Starts the host without blocking the UI thread and shows the main window, or shuts the application down with
    /// <see cref="ExitCode.Critical"/> when the configuration is invalid.
    /// </summary>
    /// <param name="e">The arguments of the start-up event.</param>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = await _startup.StartAsync(GitHubBackupHostSettings.ForCurrentProcess(), CancellationToken.None);
        if (_host is null)
        {
            Shutdown((int)ExitCode.Critical);
            return;
        }

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Closed += OnMainWindowClosed;
        window.Show();
    }

    /// <summary>
    /// Stops the host without blocking the UI thread and then shuts the application down.
    /// </summary>
    /// <param name="sender">The main window.</param>
    /// <param name="e">No data.</param>
    private async void OnMainWindowClosed(object? sender, EventArgs e)
    {
        if (_host is not null)
        {
            await AppStartup.StopAsync(_host, CancellationToken.None);
            _host = null;
        }

        Shutdown((int)ExitCode.Success);
    }
}
