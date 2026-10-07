using Microsoft.Extensions.Options;

namespace GitHubBackup.Core.Configuration;

/// <summary>
/// Validates <see cref="BackupOptions"/>; each failure names the configuration key and never its value.
/// </summary>
internal sealed class BackupOptionsValidator : IValidateOptions<BackupOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, BackupOptions options)
    {
        List<string> failures = [];

        if (string.IsNullOrWhiteSpace(options.ConfigPath))
        {
            failures.Add($"{BackupOptions.SectionName}:{nameof(BackupOptions.ConfigPath)} must not be empty.");
        }

        if (options.ShortHashLength is < BackupOptions.MinShortHashLength or > BackupOptions.MaxShortHashLength)
        {
            failures.Add(
                $"{BackupOptions.SectionName}:{nameof(BackupOptions.ShortHashLength)} must be between "
                + $"{BackupOptions.MinShortHashLength} and {BackupOptions.MaxShortHashLength}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
