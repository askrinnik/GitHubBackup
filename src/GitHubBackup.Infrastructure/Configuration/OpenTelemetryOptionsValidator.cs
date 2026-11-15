using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Validates <see cref="OpenTelemetryOptions"/>; each failure names the configuration key and never its value.
/// </summary>
/// <remarks>The values are checked only while export is enabled, so a disabled section may hold any value.</remarks>
internal sealed class OpenTelemetryOptionsValidator : IValidateOptions<OpenTelemetryOptions>
{
    /// <summary>The configuration key of <see cref="OpenTelemetryOptions.OtlpEndpoint"/>.</summary>
    private const string _endpointKey = OpenTelemetryOptions.SectionName + ":" + nameof(OpenTelemetryOptions.OtlpEndpoint);

    /// <summary>The configuration key of <see cref="OpenTelemetryOptions.Enabled"/>.</summary>
    private const string _enabledKey = OpenTelemetryOptions.SectionName + ":" + nameof(OpenTelemetryOptions.Enabled);

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpenTelemetryOptions options)
    {
        var failures = Validate(options.Enabled, options.OtlpEndpoint, options.Headers);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Checks the endpoint and the headers, for the options read at start-up and for the settings the settings editor
    /// writes.
    /// </summary>
    /// <param name="enabled">Whether telemetry is exported.</param>
    /// <param name="otlpEndpoint">The OTLP endpoint; <see langword="null"/> or empty means not set.</param>
    /// <param name="headers">The OTLP headers; <see langword="null"/>, empty or white space means not set.</param>
    /// <returns>
    /// The failures when export is enabled, each starting with the key: the endpoint is not an absolute URI, the
    /// headers are not well-formed <c>name=value</c> pairs, or the headers are set and the endpoint is neither
    /// <c>https</c> nor loopback <c>http</c>.
    /// </returns>
    public static List<string> Validate(bool enabled, string? otlpEndpoint, string? headers)
    {
        if (!enabled)
        {
            return [];
        }

        var failures = new List<string>();
        var hasHeaders = !string.IsNullOrWhiteSpace(headers);
        if (!Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out _))
        {
            failures.Add($"{_endpointKey} must be an absolute URI when {_enabledKey} is true.");
        }
        else if (hasHeaders && !SecureEndpoint.IsAllowed(otlpEndpoint))
        {
            // The headers carry the key of the receiver, which must not cross the network unencrypted.
            failures.Add(
                $"{_endpointKey} must be an absolute https URI, or http for a loopback address, when "
                + $"{OpenTelemetryOptions.HeadersKey} is set.");
        }

        if (!string.IsNullOrWhiteSpace(headers) && !OtlpHeaders.TryParse(headers, out _))
        {
            failures.Add(
                $"{OpenTelemetryOptions.HeadersKey} must be a comma-separated list of name=value pairs with a header "
                + "name and a value in each pair.");
        }

        return failures;
    }
}
