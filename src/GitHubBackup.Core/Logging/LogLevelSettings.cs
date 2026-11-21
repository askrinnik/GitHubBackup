namespace GitHubBackup.Core.Logging;

/// <summary>
/// The level of each <see cref="LogEndpoint"/> the user edits; <see langword="null"/> takes the default level of the
/// application.
/// </summary>
/// <param name="File">The level of the daily log files.</param>
/// <param name="Console">The level of the standard error of the console application.</param>
/// <param name="LiveLog">The level of the live log feed of the WPF application.</param>
/// <param name="OpenTelemetry">The level of the OTLP export of the log entries.</param>
public sealed record LogLevelSettings(
    EndpointLogLevel? File,
    EndpointLogLevel? Console,
    EndpointLogLevel? LiveLog,
    EndpointLogLevel? OpenTelemetry)
{
    /// <summary>
    /// Returns the level of <paramref name="endpoint"/>.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <returns>The level, or <see langword="null"/> when the endpoint takes the default level.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="endpoint"/> is not a defined value.</exception>
    public EndpointLogLevel? Get(LogEndpoint endpoint) => endpoint switch
    {
        LogEndpoint.File => File,
        LogEndpoint.Console => Console,
        LogEndpoint.LiveLog => LiveLog,
        LogEndpoint.OpenTelemetry => OpenTelemetry,
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "The log endpoint is not defined."),
    };
}
