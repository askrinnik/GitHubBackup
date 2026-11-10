namespace GitHubBackup.Core.Tools;

/// <summary>
/// An external tool that was found and verified.
/// </summary>
/// <param name="Path">The fully qualified path of the executable.</param>
/// <param name="Version">The version the tool reported, for example <c>2.46.0.windows.1</c>.</param>
public sealed record LocatedTool(string Path, string Version);
