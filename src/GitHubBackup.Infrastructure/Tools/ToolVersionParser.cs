using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace GitHubBackup.Infrastructure.Tools;

/// <summary>
/// Reads the version out of the text the external tools print when asked for it.
/// </summary>
/// <remarks>
/// Each method scans the lines in order and takes the first one in the expected format, so a banner or a blank line
/// before the version line does not matter.
/// </remarks>
internal static partial class ToolVersionParser
{
    /// <summary>
    /// Reads the version from the output of <c>git version</c>, for example <c>git version 2.46.0.windows.1</c>.
    /// </summary>
    /// <param name="lines">The standard output lines.</param>
    /// <param name="version">The version, or <see langword="null"/> when no line matches.</param>
    /// <returns><see langword="true"/> when a version was found.</returns>
    public static bool TryParseGit(IEnumerable<string> lines, [NotNullWhen(true)] out string? version) =>
        TryParse(lines, GitRegex(), out version);

    /// <summary>
    /// Reads the version from the output of <c>git lfs version</c>, for example <c>git-lfs/3.5.1 (GitHub; windows amd64)</c>.
    /// </summary>
    /// <param name="lines">The standard output lines.</param>
    /// <param name="version">The version, or <see langword="null"/> when no line matches.</param>
    /// <returns><see langword="true"/> when a version was found.</returns>
    public static bool TryParseGitLfs(IEnumerable<string> lines, [NotNullWhen(true)] out string? version) =>
        TryParse(lines, GitLfsRegex(), out version);

    /// <summary>
    /// Reads the version from the banner of <c>7z</c>, for example <c>7-Zip 24.08 (x64) : Copyright</c> or
    /// <c>7-Zip (a) 24.08 (x64) : Copyright</c>.
    /// </summary>
    /// <param name="lines">The standard output lines.</param>
    /// <param name="version">The version, or <see langword="null"/> when no line matches.</param>
    /// <returns><see langword="true"/> when a version was found.</returns>
    public static bool TryParseSevenZip(IEnumerable<string> lines, [NotNullWhen(true)] out string? version) =>
        TryParse(lines, SevenZipRegex(), out version);

    /// <summary>
    /// Returns the <c>version</c> group of the first line that matches <paramref name="pattern"/>.
    /// </summary>
    /// <param name="lines">The lines to scan.</param>
    /// <param name="pattern">The expression with a <c>version</c> group.</param>
    /// <param name="version">The version, or <see langword="null"/> when no line matches.</param>
    /// <returns><see langword="true"/> when a version was found.</returns>
    private static bool TryParse(IEnumerable<string> lines, Regex pattern, [NotNullWhen(true)] out string? version)
    {
        foreach (var line in lines)
        {
            var match = pattern.Match(line.Trim());
            if (match.Success)
            {
                version = match.Groups["version"].Value;
                return true;
            }
        }

        version = null;
        return false;
    }

    /// <summary>Matches the <c>git version</c> line.</summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"^git version (?<version>\d\S*)", RegexOptions.CultureInvariant)]
    private static partial Regex GitRegex();

    /// <summary>Matches the <c>git lfs version</c> line.</summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"^git-lfs/(?<version>\d\S*)", RegexOptions.CultureInvariant)]
    private static partial Regex GitLfsRegex();

    /// <summary>Matches the <c>7z</c> banner line.</summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"^7-Zip(?:\s+\([a-z]\)|\s+\[\d+\])?\s+(?<version>\d+(?:\.\d+)*)", RegexOptions.CultureInvariant)]
    private static partial Regex SevenZipRegex();
}
