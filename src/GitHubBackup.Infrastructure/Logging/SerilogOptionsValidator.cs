using Microsoft.Extensions.Options;
using Serilog.Events;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Validates <see cref="SerilogOptions"/>; each failure names the configuration key.
/// </summary>
internal sealed class SerilogOptionsValidator : IValidateOptions<SerilogOptions>
{
    /// <summary>The level names accepted in configuration, from the most to the least detailed.</summary>
    private static readonly string[] _levelNames = Enum.GetNames<LogEventLevel>();

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SerilogOptions options)
    {
        List<string> failures = [];
        if (!IsLevelName(options.Default))
        {
            failures.Add(Failure(nameof(SerilogOptions.Default)));
        }

        foreach (var (source, level) in options.Override.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!IsLevelName(level))
            {
                failures.Add(Failure($"{nameof(SerilogOptions.Override)}:{source}"));
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Converts a validated level name to a <see cref="LogEventLevel"/>.
    /// </summary>
    /// <param name="value">A value accepted by <see cref="Validate"/>.</param>
    /// <returns>The level.</returns>
    internal static LogEventLevel ParseLevel(string? value) =>
        Enum.Parse<LogEventLevel>(value ?? string.Empty, ignoreCase: true);

    /// <summary>
    /// Returns whether <paramref name="value"/> names a level, ignoring case.
    /// </summary>
    /// <remarks>Numeric values are rejected, although <see cref="Enum.Parse{TEnum}(string, bool)"/> accepts them.</remarks>
    /// <param name="value">The configured value.</param>
    /// <returns><see langword="true"/> when the value names a level.</returns>
    private static bool IsLevelName(string? value) => _levelNames.Contains(value, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates the failure message for the key <c>Serilog:MinimumLevel:<paramref name="key"/></c>.
    /// </summary>
    /// <param name="key">The key below the section.</param>
    /// <returns>The message.</returns>
    private static string Failure(string key) =>
        $"{SerilogOptions.SectionName}:{key} must be one of {string.Join(", ", _levelNames)}.";
}
