namespace GitHubBackup.Core.Tools;

/// <summary>
/// The external tools the application needs, all found and verified.
/// </summary>
/// <param name="Git">The git executable.</param>
/// <param name="GitLfsVersion">The version of git-lfs, which runs through git and has no path of its own.</param>
/// <param name="SevenZip">The 7-Zip executable.</param>
public sealed record ToolSet(LocatedTool Git, string GitLfsVersion, LocatedTool SevenZip);
