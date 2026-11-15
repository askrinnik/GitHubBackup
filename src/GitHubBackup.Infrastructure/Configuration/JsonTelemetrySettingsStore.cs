using GitHubBackup.Core.Telemetry;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Reads the telemetry settings from <see cref="OpenTelemetryOptions"/> and writes them to the <c>OpenTelemetry</c>
/// section of <see cref="GitHubBackupHostBuilder.UserSettingsFileName"/>, which the host reads at start-up right after
/// <see cref="GitHubBackupHostBuilder.AppSettingsFileName"/>.
/// </summary>
/// <remarks>
/// The <c>OpenTelemetry</c> section is replaced as a whole and the other sections of the file stay as they are; an
/// endpoint that is not set is written as <c>null</c>, which the configuration reads as not set.
/// </remarks>
/// <param name="options">The telemetry options of the running application.</param>
/// <param name="file">The user settings file.</param>
internal sealed class JsonTelemetrySettingsStore(IOptions<OpenTelemetryOptions> options, UserSettingsFile file)
    : ITelemetrySettingsStore
{
    /// <summary>
    /// The settings the last <see cref="SaveAsync"/> of this run wrote, or <see langword="null"/> before the first one.
    /// </summary>
    private TelemetrySettings? _saved;

    /// <inheritdoc />
    public TelemetrySettings Current => _saved ?? new(options.Value.Enabled, options.Value.OtlpEndpoint);

    /// <inheritdoc />
    public IReadOnlyList<string> Validate(TelemetrySettings settings)
    {
        var normalized = Normalize(settings);
        return OpenTelemetryOptionsValidator.Validate(normalized.Enabled, normalized.OtlpEndpoint);
    }

    /// <inheritdoc />
    public async Task SaveAsync(TelemetrySettings settings, CancellationToken cancellationToken)
    {
        var errors = Validate(settings);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(' ', errors), nameof(settings));
        }

        var normalized = Normalize(settings);
        await file.WriteSectionAsync(
            OpenTelemetryOptions.SectionName,
            json =>
            {
                json.WriteStartObject();
                json.WriteBoolean(nameof(OpenTelemetryOptions.Enabled), normalized.Enabled);
                if (normalized.OtlpEndpoint is null)
                {
                    json.WriteNull(nameof(OpenTelemetryOptions.OtlpEndpoint));
                }
                else
                {
                    json.WriteString(nameof(OpenTelemetryOptions.OtlpEndpoint), normalized.OtlpEndpoint);
                }

                json.WriteEndObject();
            },
            cancellationToken);
        _saved = normalized;
    }

    /// <summary>
    /// Returns <paramref name="settings"/> without white space around the endpoint, a blank endpoint being not set.
    /// </summary>
    /// <param name="settings">The settings as entered.</param>
    /// <returns>The normalized settings.</returns>
    private static TelemetrySettings Normalize(TelemetrySettings settings) =>
        settings with { OtlpEndpoint = string.IsNullOrWhiteSpace(settings.OtlpEndpoint) ? null : settings.OtlpEndpoint.Trim() };
}
