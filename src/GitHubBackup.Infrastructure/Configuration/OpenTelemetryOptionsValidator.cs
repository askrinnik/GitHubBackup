using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Validates <see cref="OpenTelemetryOptions"/>; each failure names the configuration key and never its value.
/// </summary>
/// <remarks>The endpoint is checked only while export is enabled, so a disabled section may hold any value.</remarks>
internal sealed class OpenTelemetryOptionsValidator : IValidateOptions<OpenTelemetryOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpenTelemetryOptions options)
    {
        var failures = Validate(options.Enabled, options.OtlpEndpoint);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Checks the endpoint, for the options read at start-up and for the settings the settings editor writes.
    /// </summary>
    /// <param name="enabled">Whether telemetry is exported.</param>
    /// <param name="otlpEndpoint">The OTLP endpoint; <see langword="null"/> or empty means not set.</param>
    /// <returns>The failure when export is enabled and the endpoint is not an absolute URI, starting with the key.</returns>
    public static List<string> Validate(bool enabled, string? otlpEndpoint)
    {
        if (!enabled || Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out _))
        {
            return [];
        }

        return
        [
            $"{OpenTelemetryOptions.SectionName}:{nameof(OpenTelemetryOptions.OtlpEndpoint)} must be an absolute URI when "
            + $"{OpenTelemetryOptions.SectionName}:{nameof(OpenTelemetryOptions.Enabled)} is true.",
        ];
    }
}
