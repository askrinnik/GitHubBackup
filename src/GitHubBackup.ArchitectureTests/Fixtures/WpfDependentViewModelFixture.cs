using System.Windows;

namespace GitHubBackup.ArchitectureTests.Fixtures;

/// <summary>
/// A deliberate violator of the ViewModel rule that exists only to prove the rule detects a dependency on a WPF window.
/// </summary>
public sealed class WpfDependentViewModelFixture
{
    /// <summary>Gets or sets a window, which makes this type depend on <see cref="Window"/>.</summary>
    public Window? Owner { get; set; }
}
