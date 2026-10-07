namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Holds the <c>OpenTelemetry</c> section of <c>appsettings.json</c>.
/// </summary>
public sealed class OpenTelemetryOptions
{
    /// <summary>The configuration section the options bind to.</summary>
    public const string SectionName = "OpenTelemetry";

    /// <summary>Gets or sets a value indicating whether telemetry is exported.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the OTLP endpoint that receives the telemetry when <see cref="Enabled"/> is set.</summary>
    public string? OtlpEndpoint { get; set; } = "http://localhost:4317";
}
