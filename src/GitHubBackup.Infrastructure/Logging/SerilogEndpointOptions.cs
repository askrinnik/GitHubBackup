namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Holds the levels of one log endpoint, a section <c>Serilog:Endpoints:&lt;name&gt;</c> of <c>appsettings.json</c>.
/// </summary>
/// <remarks>
/// Levels are kept as text so that <see cref="SerilogEndpointsOptionsValidator"/> reports an unknown name with the key
/// that holds it.
/// </remarks>
public sealed class SerilogEndpointOptions
{
    /// <summary>
    /// Gets or sets the level of the endpoint; <see langword="null"/> or empty takes <see cref="SerilogOptions.Default"/>,
    /// and <c>Off</c> switches the endpoint off.
    /// </summary>
    public string? Level { get; set; }

    /// <summary>
    /// Gets the levels of the endpoint by source-context prefix; they take precedence over
    /// <see cref="SerilogOptions.Override"/>, and <c>Off</c> drops the entries of that source.
    /// </summary>
    public Dictionary<string, string?> Override { get; } = new(StringComparer.OrdinalIgnoreCase);
}
