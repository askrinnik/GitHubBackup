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
        if (!options.Enabled || Uri.TryCreate(options.OtlpEndpoint, UriKind.Absolute, out _))
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"{OpenTelemetryOptions.SectionName}:{nameof(OpenTelemetryOptions.OtlpEndpoint)} must be an absolute URI when "
            + $"{OpenTelemetryOptions.SectionName}:{nameof(OpenTelemetryOptions.Enabled)} is true.");
    }
}
