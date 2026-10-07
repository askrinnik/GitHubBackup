using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Validates <see cref="HistoryOptions"/>; each failure names the configuration key and never its value.
/// </summary>
internal sealed class HistoryOptionsValidator : IValidateOptions<HistoryOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, HistoryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return string.IsNullOrWhiteSpace(options.DatabasePath)
            ? ValidateOptionsResult.Fail($"{HistoryOptions.SectionName}:{nameof(HistoryOptions.DatabasePath)} must not be empty.")
            : ValidateOptionsResult.Success;
    }
}
