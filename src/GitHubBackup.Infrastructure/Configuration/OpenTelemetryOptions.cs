namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Holds the <c>OpenTelemetry</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class OpenTelemetryOptions
{
    /// <summary>The configuration section the options bind to.</summary>
    public const string SectionName = "OpenTelemetry";

    /// <summary>The configuration key of <see cref="Headers"/>.</summary>
    public const string HeadersKey = SectionName + ":" + nameof(Headers);

    /// <summary>Gets or sets a value indicating whether telemetry is exported.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the OTLP endpoint that receives the telemetry when <see cref="Enabled"/> is set.</summary>
    public string? OtlpEndpoint { get; set; } = "http://localhost:4317";

    /// <summary>
    /// Gets or sets the OTLP headers that go with every export, as comma-separated <c>name=value</c> pairs, or
    /// <see langword="null"/> for none.
    /// </summary>
    /// <remarks>The value is a secret, such as the ingest key of the receiver, and is masked wherever it could be written.</remarks>
    public string? Headers { get; set; }
}
