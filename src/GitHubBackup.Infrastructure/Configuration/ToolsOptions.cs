namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Holds the <c>Tools</c> section of <c>appsettings.json</c>: where the external tools are installed.
/// </summary>
public sealed class ToolsOptions
{
    /// <summary>The configuration section the options bind to.</summary>
    public const string SectionName = "Tools";

    /// <summary>The path of <c>7z.exe</c> used when <see cref="SevenZipPath"/> is not set.</summary>
    public const string DefaultSevenZipPath = @"C:\Program Files\7-Zip\7z.exe";

    /// <summary>Gets or sets the absolute path of <c>git.exe</c>; <see langword="null"/> takes git from <c>PATH</c>.</summary>
    public string? GitPath { get; set; }

    /// <summary>
    /// Gets or sets the absolute path of <c>7z.exe</c>; <see langword="null"/> or empty means
    /// <see cref="DefaultSevenZipPath"/>.
    /// </summary>
    public string? SevenZipPath { get; set; } = DefaultSevenZipPath;
}
