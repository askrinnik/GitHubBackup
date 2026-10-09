namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Reports that <c>backup-config.json</c> cannot be read, or that a configuration fails validation.
/// </summary>
/// <param name="configPath">The path of the configuration file.</param>
/// <param name="errors">The errors, at least one, one sentence each.</param>
/// <param name="innerException">The exception that caused the errors, or <see langword="null"/>.</param>
public sealed class BackupConfigException(
    string configPath, IReadOnlyList<string> errors, Exception? innerException = null)
    : Exception(CreateMessage(configPath, errors), innerException)
{
    /// <summary>Gets the path of the configuration file.</summary>
    public string ConfigPath { get; } = configPath;

    /// <summary>Gets the errors, one sentence each, naming their location in the file.</summary>
    public IReadOnlyList<string> Errors { get; } = errors;

    /// <summary>
    /// Builds the exception message from the file path and the first error.
    /// </summary>
    /// <param name="configPath">The path of the configuration file.</param>
    /// <param name="errors">The errors.</param>
    /// <returns>The message.</returns>
    private static string CreateMessage(string configPath, IReadOnlyList<string> errors) => errors.Count switch
    {
        0 => $"The backup configuration '{configPath}' is invalid.",
        1 => $"The backup configuration '{configPath}' is invalid: {errors[0]}",
        _ => $"The backup configuration '{configPath}' is invalid: {errors[0]} ({errors.Count - 1} more errors)",
    };
}
