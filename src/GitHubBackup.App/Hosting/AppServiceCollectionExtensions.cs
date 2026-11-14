using GitHubBackup.App.Dialogs;
using GitHubBackup.App.Discovery;
using GitHubBackup.App.Navigation;
using GitHubBackup.App.Runs;
using GitHubBackup.App.Shell;
using GitHubBackup.App.ViewModels;
using GitHubBackup.App.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.App.Hosting;

/// <summary>
/// Registers the services of the WPF application.
/// </summary>
internal static class AppServiceCollectionExtensions
{
    /// <param name="services">The service collection of the host.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the desktop lifetime, the navigation, the dialogs, the <see cref="IDiscoveryDecisionHandlerFactory"/> and
        /// the <see cref="IBackupRunService"/> of a run started from the window, the <see cref="IUiDispatcher"/> that
        /// brings its progress to the UI thread, the main window and the ViewModels, among them the <see cref="SettingsViewModel"/> of the settings editor and the
        /// <see cref="HistoryViewModel"/> of the history screen, and the <see cref="IExplorerLauncher"/> it opens archives with.
        /// </summary>
        /// <remarks>
        /// <see cref="HostApplicationBuilder"/> registers the console lifetime even without its defaults, so it is
        /// replaced by <see cref="DesktopHostLifetime"/>.
        /// </remarks>
        /// <returns>The same service collection, for chaining.</returns>
        public IServiceCollection AddGitHubBackupApp()
        {
            services.Replace(ServiceDescriptor.Singleton<IHostLifetime, DesktopHostLifetime>());
            services.AddSingleton<INavigationService, NavigationService>();
            services.AddSingleton<IDialogService, MessageBoxDialogService>();
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();
            services.AddSingleton<IDiscoveryDialogService, WpfDiscoveryDialogService>();
            services.AddSingleton<IDiscoveryDecisionHandlerFactory, DialogDiscoveryDecisionHandlerFactory>();
            services.AddSingleton<IBackupRunService, BackupRunService>();
            services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
            services.AddTransient<RepositoriesViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddSingleton<IExplorerLauncher, ExplorerLauncher>();
            services.AddTransient<RunHistoryViewModel>();
            services.AddTransient<ArchiveHistoryViewModel>();
            services.AddTransient<HistoryViewModel>();
            return services;
        }
    }
}
