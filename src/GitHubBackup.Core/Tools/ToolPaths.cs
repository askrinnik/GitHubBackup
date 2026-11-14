namespace GitHubBackup.Core.Tools;

/// <summary>
/// The configured paths of the external tools.
/// </summary>
/// <param name="GitPath">The absolute path of <c>git.exe</c>, or <see langword="null"/> to take git from <c>PATH</c>.</param>
/// <param name="SevenZipPath">The absolute path of <c>7z.exe</c>, or <see langword="null"/> for the default location.</param>
public sealed record ToolPaths(string? GitPath, string? SevenZipPath)
{
    /// <summary>The configuration key of <see cref="GitPath"/>, which starts every error about it.</summary>
    public const string GitPathKey = "Tools:GitPath";

    /// <summary>The configuration key of <see cref="SevenZipPath"/>, which starts every error about it.</summary>
    public const string SevenZipPathKey = "Tools:SevenZipPath";
}
