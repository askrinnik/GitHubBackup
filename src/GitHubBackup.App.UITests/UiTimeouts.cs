namespace GitHubBackup.App.UITests;

/// <summary>
/// Holds the time limits the UI tests wait for the application; a wait that runs out fails the test.
/// </summary>
internal static class UiTimeouts
{
    /// <summary>The longest time the main window may take to appear after the process starts.</summary>
    public static readonly TimeSpan Launch = TimeSpan.FromSeconds(30);

    /// <summary>The longest time an element, a window or a state of the UI may take to appear after an action.</summary>
    public static readonly TimeSpan Element = TimeSpan.FromSeconds(10);

    /// <summary>The longest time a backup run with git and 7-Zip may take to show its summary.</summary>
    public static readonly TimeSpan Run = TimeSpan.FromMinutes(2);

    /// <summary>The longest time the process may take to exit after its last window closes.</summary>
    public static readonly TimeSpan Exit = TimeSpan.FromSeconds(30);

    /// <summary>The time between two checks of a condition the tests wait for.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);
}
