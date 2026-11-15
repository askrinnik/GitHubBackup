using GitHubBackup.Core.Processes;

namespace GitHubBackup.Infrastructure.Processes;

/// <summary>
/// Builds the environment variables a process receives as text for the log.
/// </summary>
/// <remarks>
/// Values are written as they are, without quotes or escaping: the text is only read, never parsed back. The caller
/// still masks the whole text, so a secret that reaches a variable not marked as secret is hidden as well.
/// </remarks>
internal static class ProcessEnvironmentFormat
{
    /// <summary>The text written instead of the value of a secret variable.</summary>
    internal const string SecretPlaceholder = "***";

    /// <summary>
    /// Returns the variables as <c>NAME=value</c> pairs joined by a comma and a space; a secret variable shows
    /// <see cref="SecretPlaceholder"/> instead of its value, even when the value is empty.
    /// </summary>
    /// <param name="variables">The environment variables of the process.</param>
    /// <returns>The pairs, or an empty string when there are no variables.</returns>
    public static string Format(IReadOnlyList<ProcessEnvironmentVariable> variables) =>
        string.Join(
            ", ",
            variables.Select(variable => $"{variable.Name}={(variable.IsSecret ? SecretPlaceholder : variable.Value)}"));
}
