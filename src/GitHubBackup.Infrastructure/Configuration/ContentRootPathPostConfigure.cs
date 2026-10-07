using GitHubBackup.Core.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Makes the file paths of <see cref="BackupOptions"/> and <see cref="HistoryOptions"/> absolute by resolving
/// relative values against the content root, the folder of the executable.
/// </summary>
/// <remarks>
/// The application is started from shortcuts, the scheduler and other folders, so a path in
/// <c>appsettings.json</c> must not depend on the current directory. Empty values are left for the validators to report.
/// </remarks>
/// <param name="environment">The host environment that supplies the content root.</param>
internal sealed class ContentRootPathPostConfigure(IHostEnvironment environment)
    : IPostConfigureOptions<BackupOptions>, IPostConfigureOptions<HistoryOptions>
{
    /// <inheritdoc />
    public void PostConfigure(string? name, BackupOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.ConfigPath = Resolve(options.ConfigPath);
    }

    /// <inheritdoc />
    public void PostConfigure(string? name, HistoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.DatabasePath = Resolve(options.DatabasePath);
    }

    /// <summary>
    /// Returns <paramref name="path"/> as a full path, resolved against the content root when it is relative.
    /// </summary>
    /// <param name="path">The configured path.</param>
    /// <returns>The full path, or <paramref name="path"/> unchanged when it is empty.</returns>
    private string Resolve(string path) =>
        string.IsNullOrWhiteSpace(path) ? path : Path.GetFullPath(path, environment.ContentRootPath);
}
