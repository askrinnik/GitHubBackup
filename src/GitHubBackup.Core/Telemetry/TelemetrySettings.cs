namespace GitHubBackup.Core.Telemetry;

/// <summary>
/// The telemetry settings the user edits.
/// </summary>
/// <param name="Enabled">Whether traces and metrics are exported over OTLP.</param>
/// <param name="OtlpEndpoint">
/// The OTLP endpoint (gRPC) that receives the telemetry, or <see langword="null"/> when none is set.
/// </param>
/// <remarks>
/// The keys are <see langword="static"/> read-only fields, not constants: a constant string that starts with the name
/// of the OpenTelemetry SDK namespace counts as a dependency of <c>Core</c> on the SDK in the layer rules.
/// </remarks>
public sealed record TelemetrySettings(bool Enabled, string? OtlpEndpoint)
{
    /// <summary>The configuration key of <see cref="Enabled"/>, which starts every error about it.</summary>
    public static readonly string EnabledKey = "OpenTelemetry:Enabled";

    /// <summary>The configuration key of <see cref="OtlpEndpoint"/>, which starts every error about it.</summary>
    public static readonly string OtlpEndpointKey = "OpenTelemetry:OtlpEndpoint";
}
