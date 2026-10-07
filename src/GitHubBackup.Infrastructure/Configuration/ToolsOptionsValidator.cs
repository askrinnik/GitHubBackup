using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Validates <see cref="ToolsOptions"/>; each failure names the configuration key and never its value.
/// </summary>
/// <remarks>
/// Only the form of the paths is checked here; whether the files exist is checked when the tools are located.
/// A relative tool path is rejected so that a tool is never picked up from the current directory.
/// </remarks>
internal sealed class ToolsOptionsValidator : IValidateOptions<ToolsOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ToolsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];
        AddFailureIfRelative(failures, options.GitPath, nameof(ToolsOptions.GitPath));
        AddFailureIfRelative(failures, options.SevenZipPath, nameof(ToolsOptions.SevenZipPath));

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Adds a failure for <paramref name="key"/> when <paramref name="path"/> is set but not fully qualified.
    /// </summary>
    /// <param name="failures">The list that collects the failures.</param>
    /// <param name="path">The configured path; <see langword="null"/> or empty means not set.</param>
    /// <param name="key">The property name within the <c>Tools</c> section.</param>
    private static void AddFailureIfRelative(List<string> failures, string? path, string key)
    {
        if (!string.IsNullOrEmpty(path) && !Path.IsPathFullyQualified(path))
        {
            failures.Add($"{ToolsOptions.SectionName}:{key} must be an absolute path when set.");
        }
    }
}
