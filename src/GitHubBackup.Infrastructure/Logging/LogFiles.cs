using System.Globalization;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Names the daily log files: <c>logs\githubbackup-yyyyMMdd.log</c> (text) and <c>logs\githubbackup-yyyyMMdd.json</c>
/// (CLEF, one JSON object per line), in the content root.
/// </summary>
public static class LogFiles
{
    /// <summary>The name of the log folder in the content root.</summary>
    public const string FolderName = "logs";

    /// <summary>The start of every log file name; the date follows it.</summary>
    public const string FileNamePrefix = "githubbackup-";

    /// <summary>The extension of the text log.</summary>
    public const string TextExtension = ".log";

    /// <summary>The extension of the structured log.</summary>
    public const string JsonExtension = ".json";

    /// <summary>
    /// Returns the path that the rolling file sink receives; the sink inserts the date before the extension.
    /// </summary>
    /// <param name="contentRootPath">The folder of the executable.</param>
    /// <param name="extension"><see cref="TextExtension"/> or <see cref="JsonExtension"/>.</param>
    /// <returns>The path, for example <c>C:\app\logs\githubbackup-.log</c>.</returns>
    public static string GetPathTemplate(string contentRootPath, string extension) =>
        Path.Combine(contentRootPath, FolderName, FileNamePrefix + extension);

    /// <summary>
    /// Returns the name of the log file for <paramref name="date"/>.
    /// </summary>
    /// <param name="date">The local date the entries were written on.</param>
    /// <param name="extension"><see cref="TextExtension"/> or <see cref="JsonExtension"/>.</param>
    /// <returns>The file name, for example <c>githubbackup-20261007.log</c>.</returns>
    public static string GetFileName(DateOnly date, string extension) =>
        FileNamePrefix + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + extension;
}
