using System.Text;

namespace GitHubBackup.Core.Telemetry;

/// <summary>
/// The telemetry settings the user edits.
/// </summary>
/// <param name="Enabled">Whether traces and metrics are exported over OTLP.</param>
/// <param name="OtlpEndpoint">
/// The OTLP endpoint (gRPC) that receives the telemetry, or <see langword="null"/> when none is set.
/// </param>
/// <param name="Headers">
/// The OTLP headers as comma-separated <c>name=value</c> pairs, or <see langword="null"/> when none are set. The value
/// is a secret, such as the key of the receiver; it has no default so that a caller cannot drop it from the file by
/// leaving it out.
/// </param>
/// <remarks>
/// The keys are <see langword="static"/> read-only fields, not constants: a constant string that starts with the name
/// of the OpenTelemetry SDK namespace counts as a dependency of <c>Core</c> on the SDK in the layer rules.
/// </remarks>
public sealed record TelemetrySettings(bool Enabled, string? OtlpEndpoint, string? Headers)
{
    /// <summary>The configuration key of <see cref="Enabled"/>, which starts every error about it.</summary>
    public static readonly string EnabledKey = "OpenTelemetry:Enabled";

    /// <summary>The configuration key of <see cref="OtlpEndpoint"/>, which starts every error about it.</summary>
    public static readonly string OtlpEndpointKey = "OpenTelemetry:OtlpEndpoint";

    /// <summary>The configuration key of <see cref="Headers"/>, which starts every error about it.</summary>
    public static readonly string HeadersKey = "OpenTelemetry:Headers";

    /// <summary>
    /// Writes the flag and the endpoint, and whether headers are set but not their value, into the text of
    /// <see cref="object.ToString"/>.
    /// </summary>
    /// <param name="builder">The builder of the text.</param>
    /// <returns><see langword="true"/>, because members were written.</returns>
    /// <remarks>
    /// The compiler-generated <see cref="object.ToString"/> of the record calls this method in place of the generated
    /// one, which would print the headers.
    /// </remarks>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(nameof(Enabled)).Append(" = ").Append(Enabled)
            .Append(", ").Append(nameof(OtlpEndpoint)).Append(" = ").Append(OtlpEndpoint)
            .Append(", ").Append(nameof(Headers)).Append(" = ").Append(Headers is null ? "<not set>" : "***");
        return true;
    }
}
