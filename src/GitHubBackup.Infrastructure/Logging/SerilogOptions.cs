namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Holds the <c>Serilog:MinimumLevel</c> section of <c>appsettings.json</c>: the levels below which log entries are
/// dropped.
/// </summary>
/// <remarks>
/// Levels are kept as text so that an unknown name is reported by <see cref="SerilogOptionsValidator"/> with the key
/// that holds it, like every other configuration error.
/// </remarks>
public sealed class SerilogOptions
{
    /// <summary>The configuration section the options bind to.</summary>
    public const string SectionName = "Serilog:MinimumLevel";

    /// <summary>Gets or sets the minimum level of every source without an override.</summary>
    public string? Default { get; set; } = "Information";

    /// <summary>
    /// Gets the minimum levels by source-context prefix, for example <c>Microsoft</c> → <c>Warning</c>.
    /// </summary>
    public Dictionary<string, string?> Override { get; } = new(StringComparer.OrdinalIgnoreCase);
}
