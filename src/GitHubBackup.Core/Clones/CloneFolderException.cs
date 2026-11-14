namespace GitHubBackup.Core.Clones;

/// <summary>
/// Reports that the clone folder of a repository or wiki cannot be used or moved: it is not a clone of that
/// repository, or the clone cannot be moved to its configured folder. The folder is left as it is.
/// </summary>
/// <param name="message">The message, naming the folder and what the user can do.</param>
/// <param name="folder">The folder involved.</param>
public sealed class CloneFolderException(string message, string folder) : Exception(message)
{
    /// <summary>Gets the folder involved.</summary>
    public string Folder { get; } = folder;
}
