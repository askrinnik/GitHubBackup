using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GitHubBackup.ArchitectureTests.Fixtures;

/// <summary>
/// A ViewModel with a command, to prove that the ViewModel rule allows <see cref="ICommand"/> from
/// <c>System.Windows.Input</c>.
/// </summary>
public sealed class RelayCommandViewModelFixture : ObservableObject
{
    /// <summary>Gets a command that does nothing.</summary>
    public ICommand RefreshCommand { get; } = new RelayCommand(static () => { });
}
