using GitHubBackup.App;

namespace GitHubBackup.ArchitectureTests.Fixtures;

/// <summary>
/// A deliberate violator of the layer rule that exists only to prove the rules detect a dependency on the host.
/// </summary>
public sealed class HostDependentFixture
{
    /// <summary>
    /// Gets or sets a host window, which makes this type depend on the <c>GitHubBackup.App</c> namespace and assembly.
    /// </summary>
    public MainWindow? Window { get; set; }
}
