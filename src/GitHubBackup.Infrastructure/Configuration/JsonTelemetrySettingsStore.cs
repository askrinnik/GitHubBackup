using GitHubBackup.Core.Security;
using GitHubBackup.Core.Telemetry;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Reads the telemetry settings from <see cref="OpenTelemetryOptions"/>, the application settings file and the
/// <see cref="IOtlpHeadersStore"/>, and writes the flag and the endpoint to the <c>OpenTelemetry</c> section of
/// <see cref="GitHubBackupHostBuilder.AppSettingsFileName"/> and the headers to the store.
/// </summary>
/// <remarks>
/// A value that is not set is written to the file as <c>null</c>, which the configuration reads as not set. The
/// headers are taken from the store, or from the file when the store holds none, so a key that an environment
/// variable, the command line or user secrets set never reaches the store through a save.
/// </remarks>
internal sealed class JsonTelemetrySettingsStore : ITelemetrySettingsStore
{
    /// <summary>The telemetry options of the running application.</summary>
    private readonly IOptions<OpenTelemetryOptions> _options;

    /// <summary>The settings file of the application.</summary>
    private readonly AppSettingsFile _file;

    /// <summary>The store that keeps the headers.</summary>
    private readonly IOtlpHeadersStore _headersStore;

    /// <summary>The masker of the host, which learns every header list the store holds or writes.</summary>
    private readonly ISecretMasker _masker;

    /// <summary>
    /// The headers read at start-up: those of the header store, or those of the application settings file when the
    /// store holds none; <see langword="null"/> when neither sets them.
    /// </summary>
    private readonly string? _startupHeaders;

    /// <summary>
    /// The settings the last <see cref="SaveAsync"/> of this run wrote, or <see langword="null"/> before the first one.
    /// </summary>
    private TelemetrySettings? _saved;

    /// <summary>
    /// Creates the store and registers the headers of the application settings file and of the header store with
    /// <paramref name="masker"/>.
    /// </summary>
    /// <remarks>
    /// The masker of the host knows only the effective headers; when another source overrides them, the saved values
    /// are registered here, since the settings editor holds them in memory.
    /// </remarks>
    /// <param name="options">The telemetry options of the running application.</param>
    /// <param name="configuration">The application configuration, which tells the saved headers apart from their overrides.</param>
    /// <param name="masker">The masker of the host.</param>
    /// <param name="file">The settings file of the application.</param>
    /// <param name="headersStore">The store that keeps the headers.</param>
    public JsonTelemetrySettingsStore(
        IOptions<OpenTelemetryOptions> options,
        IConfiguration configuration,
        ISecretMasker masker,
        AppSettingsFile file,
        IOtlpHeadersStore headersStore)
    {
        _options = options;
        _file = file;
        _headersStore = headersStore;
        _masker = masker;
        var (fileHeaders, credentialHeaders, overridden) = ConfigurationLayers.FindHeadersLayers(configuration);
        var savedFileHeaders = NullIfBlank(fileHeaders);
        var savedCredentialHeaders = NullIfBlank(credentialHeaders);
        HostSecretMasker.RegisterHeaders(masker, savedFileHeaders);
        HostSecretMasker.RegisterHeaders(masker, savedCredentialHeaders);
        HeadersOverridden = overridden;
        HeadersInAppSettings = savedFileHeaders is not null;
        _startupHeaders = savedCredentialHeaders ?? savedFileHeaders;
    }

    /// <inheritdoc />
    public TelemetrySettings Current =>
        _saved ?? new(_options.Value.Enabled, _options.Value.OtlpEndpoint, _startupHeaders);

    /// <inheritdoc />
    public bool HeadersOverridden { get; }

    /// <inheritdoc />
    public bool HeadersInAppSettings { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// The headers that take effect are those of the overriding source when there is one, so the endpoint is checked
    /// against them; a non-empty field is checked as well, format and endpoint rule alike, since it goes into the header store
    /// and takes effect once the overriding source is gone.
    /// </remarks>
    public IReadOnlyList<string> Validate(TelemetrySettings settings)
    {
        var normalized = Normalize(settings);
        var effectiveHeaders = HeadersOverridden ? _options.Value.Headers : normalized.Headers;
        var errors = OpenTelemetryOptionsValidator.Validate(normalized.Enabled, normalized.OtlpEndpoint, effectiveHeaders);
        if (HeadersOverridden && normalized.Headers is not null)
        {
            errors.AddRange(OpenTelemetryOptionsValidator.Validate(normalized.Enabled, normalized.OtlpEndpoint, normalized.Headers));
        }

        return [.. errors.Distinct(StringComparer.Ordinal)];
    }

    /// <inheritdoc />
    /// <remarks>
    /// The headers are registered with the masker before they are checked or written, so that they stay masked in any
    /// error the save reports. The header store is written before the file, so a failure of the store leaves the file
    /// as it is; a failure of the file after that leaves the headers saved, and the next save writes both again.
    /// </remarks>
    public async Task SaveAsync(TelemetrySettings settings, CancellationToken cancellationToken)
    {
        var normalized = Normalize(settings);
        HostSecretMasker.RegisterHeaders(_masker, normalized.Headers);

        var errors = Validate(normalized);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(' ', errors), nameof(settings));
        }

        if (normalized.Headers is null)
        {
            await _headersStore.DeleteAsync(cancellationToken);
        }
        else
        {
            await _headersStore.WriteAsync(normalized.Headers, cancellationToken);
        }

        await _file.WriteSectionValuesAsync(
            OpenTelemetryOptions.SectionName,
            [
                AppSettingValue.FromFlag(nameof(OpenTelemetryOptions.Enabled), normalized.Enabled),
                AppSettingValue.FromText(nameof(OpenTelemetryOptions.OtlpEndpoint), normalized.OtlpEndpoint),
                AppSettingValue.Remove(nameof(OpenTelemetryOptions.Headers)),
            ],
            cancellationToken);
        _saved = normalized;
        HeadersInAppSettings = false;
    }

    /// <summary>
    /// Returns <paramref name="settings"/> without white space around the endpoint and the headers, a blank value
    /// being not set.
    /// </summary>
    /// <param name="settings">The settings as entered.</param>
    /// <returns>The normalized settings.</returns>
    private static TelemetrySettings Normalize(TelemetrySettings settings) =>
        settings with { OtlpEndpoint = NullIfBlank(settings.OtlpEndpoint), Headers = NullIfBlank(settings.Headers) };

    /// <summary>
    /// Returns <paramref name="value"/> trimmed, or <see langword="null"/> when it is empty or white space.
    /// </summary>
    /// <param name="value">The value as entered or read.</param>
    /// <returns>The trimmed value or <see langword="null"/>.</returns>
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
