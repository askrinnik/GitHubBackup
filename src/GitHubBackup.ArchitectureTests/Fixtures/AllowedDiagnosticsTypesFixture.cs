using System.Diagnostics;

namespace GitHubBackup.ArchitectureTests.Fixtures;

/// <summary>
/// Uses only the <c>System.Diagnostics</c> types the process rule allows, including one whose name starts like a
/// forbidden type, to prove the rule matches whole type names.
/// </summary>
public static class AllowedDiagnosticsTypesFixture
{
    /// <summary>
    /// Measures nothing and returns a window style, touching <see cref="Stopwatch"/> and
    /// <see cref="ProcessWindowStyle"/>.
    /// </summary>
    /// <returns>The hidden window style.</returns>
    public static ProcessWindowStyle Measure()
    {
        var stopwatch = Stopwatch.StartNew();
        stopwatch.Stop();
        return ProcessWindowStyle.Hidden;
    }
}
