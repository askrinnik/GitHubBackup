using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.Cli;

/// <summary>
/// Locates the text log of the current day.
/// </summary>
internal static class CliLogPath
{
    /// <summary>
    /// Returns the path of the text log of the current local day.
    /// </summary>
    /// <param name="environment">The host environment whose content root holds the log folder.</param>
    /// <param name="timeProvider">Supplies the local date that names the log file.</param>
    /// <returns>The absolute path of the log file.</returns>
    public static string Get(IHostEnvironment environment, TimeProvider timeProvider) => Path.Combine(
        environment.ContentRootPath,
        LogFiles.FolderName,
        LogFiles.GetFileName(DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime), LogFiles.TextExtension));
}
