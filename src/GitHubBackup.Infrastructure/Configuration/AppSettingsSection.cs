namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// A section of <c>appsettings.json</c> with the keys <see cref="AppSettingsFile"/> sets in it.
/// </summary>
/// <param name="Path">
/// The canonical path of the section, its names separated by <c>:</c> as in the configuration, for example
/// <c>Tools</c> or <c>Serilog:Endpoints:File</c>.
/// </param>
/// <param name="Values">The keys with their new values.</param>
internal sealed record AppSettingsSection(string Path, IReadOnlyList<AppSettingValue> Values);
