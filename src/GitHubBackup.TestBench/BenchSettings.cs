using GitHubBackup.TestUtilities;

namespace GitHubBackup.TestBench;

/// <summary>
/// Holds what one run of the test bench prepares and whether it starts the app.
/// </summary>
/// <param name="Scenario">The scenario of the API stub and of <c>backup-config.json</c>.</param>
/// <param name="AppPath">The absolute path of the folder the desktop app is copied from.</param>
/// <param name="UiSettings">The content of <c>ui-settings.json</c>; nothing is written when it sets no value.</param>
/// <param name="Launch">Whether the bench starts the app after preparing the folder.</param>
/// <param name="Keep">Whether the temporary folder stays in place when the bench stops.</param>
internal sealed record BenchSettings(UiScenario Scenario, string AppPath, UiSettingsJson UiSettings, bool Launch, bool Keep);
