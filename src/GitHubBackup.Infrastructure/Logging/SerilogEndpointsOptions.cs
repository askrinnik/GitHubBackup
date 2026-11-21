using GitHubBackup.Core.Logging;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Holds the <c>Serilog:Endpoints</c> section of <c>appsettings.json</c>: the levels of each <see cref="LogEndpoint"/>.
/// </summary>
public sealed class SerilogEndpointsOptions
{
    /// <summary>The configuration section the options bind to.</summary>
    public const string SectionName = "Serilog:Endpoints";

    /// <summary>
    /// The level of the <see cref="File"/> endpoint when its section has no <c>Level</c> key, so the files hold the
    /// details of the tool runs and API requests while the other endpoints get only their descriptions.
    /// </summary>
    public const string DefaultFileLevel = "Debug";

    /// <summary>Gets the levels of the daily log files.</summary>
    public SerilogEndpointOptions File { get; } = new();

    /// <summary>Gets the levels of the standard error of the console application.</summary>
    public SerilogEndpointOptions Console { get; } = new();

    /// <summary>Gets the levels of the live log feed of the WPF application.</summary>
    public SerilogEndpointOptions LiveLog { get; } = new();

    /// <summary>Gets the levels of the OTLP export.</summary>
    public SerilogEndpointOptions OpenTelemetry { get; } = new();

    /// <summary>
    /// Returns the levels of <paramref name="endpoint"/>.
    /// </summary>
    /// <param name="endpoint">The endpoint.</param>
    /// <returns>The options of the endpoint.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="endpoint"/> is not a defined value.</exception>
    public SerilogEndpointOptions Get(LogEndpoint endpoint) => endpoint switch
    {
        LogEndpoint.File => File,
        LogEndpoint.Console => Console,
        LogEndpoint.LiveLog => LiveLog,
        LogEndpoint.OpenTelemetry => OpenTelemetry,
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, "The log endpoint is not defined."),
    };
}
