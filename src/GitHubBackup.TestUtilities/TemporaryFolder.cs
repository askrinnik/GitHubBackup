namespace GitHubBackup.TestUtilities;

/// <summary>
/// Creates and deletes unique folders under the system temp folder for tests.
/// </summary>
public static class TemporaryFolder
{
    private const int _deleteAttempts = 5;

    /// <summary>
    /// Creates a new empty folder with a unique name.
    /// </summary>
    /// <returns>The absolute path of the folder.</returns>
    public static string Create()
    {
        var path = Path.Combine(Path.GetTempPath(), "GitHubBackup.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Deletes a folder tree, including read-only files such as git object files. A missing folder is not an error.
    /// </summary>
    /// <param name="path">The absolute path of the folder.</param>
    public static void Delete(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                ClearReadOnly(path);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException && attempt < _deleteAttempts)
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    /// <summary>
    /// Removes the read-only attribute from every file under <paramref name="path"/>.
    /// </summary>
    /// <param name="path">The root folder.</param>
    private static void ClearReadOnly(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }
    }
}
