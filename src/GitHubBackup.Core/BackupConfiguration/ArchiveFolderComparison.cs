namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Compares archive folders the way both hosts present them to the user.
/// </summary>
public static class ArchiveFolderComparison
{
    /// <summary>
    /// Returns whether two archive folders are the same folder.
    /// </summary>
    /// <param name="first">The first folder.</param>
    /// <param name="second">The second folder.</param>
    /// <returns><see langword="true"/> when they differ at most in case and a trailing separator.</returns>
    public static bool IsSameFolder(string first, string second) =>
        string.Equals(
            first.TrimEnd('\\', '/'),
            second.TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);
}
