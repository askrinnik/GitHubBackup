using GitHubBackup.Core.Ui;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// How the main window opens.
/// </summary>
/// <param name="Bounds">The normal bounds to apply, or <see langword="null"/> for the default position and size.</param>
/// <param name="IsMaximized">Whether the window opens maximized.</param>
public sealed record StartupPlacement(WindowPlacement? Bounds, bool IsMaximized);
