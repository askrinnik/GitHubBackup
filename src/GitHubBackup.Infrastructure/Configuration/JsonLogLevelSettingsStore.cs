using GitHubBackup.Core.Logging;
using GitHubBackup.Infrastructure.Hosting;
using GitHubBackup.Infrastructure.Logging;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Reads the endpoint levels from <see cref="SerilogEndpointsOptions"/> and <see cref="SerilogOptions"/>, and writes
/// them to the <c>Level</c> key of each <c>Serilog:Endpoints:&lt;name&gt;</c> section of
/// <see cref="GitHubBackupHostBuilder.AppSettingsFileName"/>.
/// </summary>
/// <remarks>
/// A level that is not set is written as <c>null</c>, which the configuration reads as not set. Only the
/// <c>Level</c> keys are written, so the <c>Override</c> keys of the endpoints stay as the user wrote them.
/// </remarks>
/// <param name="options">The minimum levels of the running application.</param>
/// <param name="endpoints">The endpoint levels of the running application.</param>
/// <param name="file">The settings file of the application.</param>
internal sealed class JsonLogLevelSettingsStore(
    IOptions<SerilogOptions> options,
    IOptions<SerilogEndpointsOptions> endpoints,
    AppSettingsFile file) : ILogLevelSettingsStore
{
    /// <summary>
    /// The levels the last <see cref="SaveAsync"/> of this run wrote, or <see langword="null"/> before the first one.
    /// </summary>
    private LogLevelSettings? _saved;

    /// <inheritdoc />
    public LogLevelSettings Current => _saved ?? new(
        Parse(endpoints.Value.File.Level),
        Parse(endpoints.Value.Console.Level),
        Parse(endpoints.Value.LiveLog.Level),
        Parse(endpoints.Value.OpenTelemetry.Level));

    /// <inheritdoc />
    /// <remarks>The start-up validation rejects <c>Off</c> as the default level, so it is never returned.</remarks>
    public EndpointLogLevel DefaultLevel => Parse(options.Value.Default) ?? EndpointLogLevel.Information;

    /// <inheritdoc />
    public async Task SaveAsync(LogLevelSettings settings, CancellationToken cancellationToken)
    {
        await file.WriteSectionsAsync(
            [.. Enum.GetValues<LogEndpoint>().Select(endpoint => new AppSettingsSection(
                $"{SerilogEndpointsOptions.SectionName}:{endpoint}",
                [AppSettingValue.FromText(nameof(SerilogEndpointOptions.Level), settings.Get(endpoint)?.ToString())]))],
            cancellationToken);
        _saved = settings;
    }

    /// <summary>
    /// Converts a configured level name to an <see cref="EndpointLogLevel"/>.
    /// </summary>
    /// <param name="value">The configured name, which the start-up validation has checked.</param>
    /// <returns>The level, or <see langword="null"/> when the value is not set or names no endpoint level.</returns>
    private static EndpointLogLevel? Parse(string? value) =>
        LogLevels.IsEndpointName(value) && Enum.TryParse<EndpointLogLevel>(value, ignoreCase: true, out var level)
            ? level
            : null;
}
