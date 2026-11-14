using GitHubBackup.Core.Ui;

namespace GitHubBackup.App.Shell;

/// <summary>
/// Answers questions about the connected monitors, so that ViewModels check a saved window position without WPF.
/// </summary>
public interface IMonitorLayout
{
    /// <summary>
    /// Returns whether the bounds of <paramref name="placement"/> overlap at least one connected monitor.
    /// </summary>
    /// <param name="placement">The placement, in device-independent units.</param>
    /// <returns><see langword="true"/> when a window with these bounds would be visible.</returns>
    bool IsOnAnyMonitor(WindowPlacement placement);
}
