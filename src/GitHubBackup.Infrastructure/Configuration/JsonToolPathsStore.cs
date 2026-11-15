using GitHubBackup.Core.Tools;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Reads the tool paths from <see cref="ToolsOptions"/> and writes them to the <c>Tools</c> section of
/// <see cref="GitHubBackupHostBuilder.AppSettingsFileName"/>.
/// </summary>
/// <remarks>
/// Only <c>GitPath</c> and <c>SevenZipPath</c> are written; a path that is not set is written as <c>null</c>, which
/// the configuration reads as not set.
/// </remarks>
/// <param name="options">The tool paths of the running application.</param>
/// <param name="file">The settings file of the application.</param>
internal sealed class JsonToolPathsStore(IOptions<ToolsOptions> options, AppSettingsFile file) : IToolPathsStore
{
    /// <summary>
    /// The paths the last <see cref="SaveAsync"/> of this run wrote, or <see langword="null"/> before the first one.
    /// </summary>
    private ToolPaths? _saved;

    /// <inheritdoc />
    public ToolPaths Current => _saved ?? new(options.Value.GitPath, options.Value.SevenZipPath);

    /// <inheritdoc />
    public IReadOnlyList<string> Validate(ToolPaths paths)
    {
        var normalized = Normalize(paths);
        return ToolsOptionsValidator.Validate(normalized.GitPath, normalized.SevenZipPath);
    }

    /// <inheritdoc />
    public async Task SaveAsync(ToolPaths paths, CancellationToken cancellationToken)
    {
        var errors = Validate(paths);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(' ', errors), nameof(paths));
        }

        var normalized = Normalize(paths);
        await file.WriteSectionValuesAsync(
            ToolsOptions.SectionName,
            [
                AppSettingValue.FromText(nameof(ToolsOptions.GitPath), normalized.GitPath),
                AppSettingValue.FromText(nameof(ToolsOptions.SevenZipPath), normalized.SevenZipPath),
            ],
            cancellationToken);
        _saved = normalized;
    }

    /// <summary>
    /// Returns <paramref name="paths"/> without surrounding white space, a blank path being not set.
    /// </summary>
    /// <param name="paths">The paths as entered.</param>
    /// <returns>The normalized paths.</returns>
    private static ToolPaths Normalize(ToolPaths paths) => new(Trim(paths.GitPath), Trim(paths.SevenZipPath));

    /// <summary>
    /// Returns <paramref name="path"/> without surrounding white space, or <see langword="null"/> when it is blank.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The trimmed path, or <see langword="null"/>.</returns>
    private static string? Trim(string? path) => string.IsNullOrWhiteSpace(path) ? null : path.Trim();
}
