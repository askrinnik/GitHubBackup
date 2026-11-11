namespace GitHubBackup.Core.State;

/// <summary>
/// Reports that the state file of a repository cannot be read, or that an archive cannot be recorded in it.
/// </summary>
/// <param name="statePath">The path of the state file.</param>
/// <param name="errors">The errors, at least one, one sentence each.</param>
/// <param name="innerException">The exception that caused the errors, or <see langword="null"/>.</param>
public sealed class BackupStateException(
    string statePath, IReadOnlyList<string> errors, Exception? innerException = null)
    : Exception(CreateMessage(statePath, errors), innerException)
{
    /// <summary>Gets the path of the state file.</summary>
    public string StatePath { get; } = statePath;

    /// <summary>Gets the errors, one sentence each.</summary>
    public IReadOnlyList<string> Errors { get; } = errors;

    /// <summary>
    /// Builds the exception message from the file path and the first error.
    /// </summary>
    /// <param name="statePath">The path of the state file.</param>
    /// <param name="errors">The errors.</param>
    /// <returns>The message.</returns>
    private static string CreateMessage(string statePath, IReadOnlyList<string> errors) => errors.Count switch
    {
        0 => $"The backup state '{statePath}' is invalid.",
        1 => $"The backup state '{statePath}' is invalid: {errors[0]}",
        _ => $"The backup state '{statePath}' is invalid: {errors[0]} ({errors.Count - 1} more errors)",
    };
}
