using System.Text.Json;
using GitHubBackup.Core.Tools;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Reads the tool paths from <see cref="ToolsOptions"/> and writes them to the <c>Tools</c> section of
/// <see cref="GitHubBackupHostBuilder.UserSettingsFileName"/>, which the host reads at start-up right after
/// <see cref="GitHubBackupHostBuilder.AppSettingsFileName"/>.
/// </summary>
/// <remarks>
/// The <c>Tools</c> section is replaced as a whole and the other sections of the file stay as they are; a path that
/// is not set is written as <c>null</c>, which the configuration reads as not set, so it also clears a path set in
/// <see cref="GitHubBackupHostBuilder.AppSettingsFileName"/>.
/// </remarks>
/// <param name="options">The tool paths of the running application.</param>
/// <param name="file">The user settings file.</param>
internal sealed class JsonToolPathsStore(IOptions<ToolsOptions> options, UserSettingsFile file) : IToolPathsStore
{
    /// <inheritdoc />
    public ToolPaths Current => new(options.Value.GitPath, options.Value.SevenZipPath);

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
        await file.WriteSectionAsync(
            ToolsOptions.SectionName,
            json =>
            {
                json.WriteStartObject();
                WritePath(json, nameof(ToolsOptions.GitPath), normalized.GitPath);
                WritePath(json, nameof(ToolsOptions.SevenZipPath), normalized.SevenZipPath);
                json.WriteEndObject();
            },
            cancellationToken);
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

    /// <summary>
    /// Writes one path, or <c>null</c> when it is not set.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="name">The property name.</param>
    /// <param name="path">The normalized path, or <see langword="null"/> when it is not set.</param>
    private static void WritePath(Utf8JsonWriter json, string name, string? path)
    {
        if (path is null)
        {
            json.WriteNull(name);
        }
        else
        {
            json.WriteString(name, path);
        }
    }
}
