using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Validates <see cref="SerilogOptions"/>; each failure names the configuration key.
/// </summary>
/// <remarks>
/// <see cref="LogLevels.Off"/> is rejected here: it is meaningful only for a single endpoint of
/// <see cref="SerilogEndpointsOptions"/>.
/// </remarks>
internal sealed class SerilogOptionsValidator : IValidateOptions<SerilogOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SerilogOptions options)
    {
        List<string> failures = [];
        if (!LogLevels.IsName(options.Default))
        {
            failures.Add(Failure(nameof(SerilogOptions.Default)));
        }

        foreach (var (source, level) in options.Override.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!LogLevels.IsName(level))
            {
                failures.Add(Failure($"{nameof(SerilogOptions.Override)}:{source}"));
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Creates the failure message for the key <c>Serilog:MinimumLevel:<paramref name="key"/></c>.
    /// </summary>
    /// <param name="key">The key below the section.</param>
    /// <returns>The message.</returns>
    private static string Failure(string key) =>
        $"{SerilogOptions.SectionName}:{key} must be one of {string.Join(", ", LogLevels.Names)}.";
}
