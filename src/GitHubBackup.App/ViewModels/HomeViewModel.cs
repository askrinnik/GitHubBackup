using CommunityToolkit.Mvvm.ComponentModel;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs the home view, a placeholder until the screens of the application exist.
/// </summary>
public sealed class HomeViewModel : ObservableObject
{
    /// <summary>The text that the home view shows.</summary>
    public const string PlaceholderText = "GitHub Backup is ready. The screens of the application are not available yet.";

    /// <summary>Gets the text that the home view shows.</summary>
    public string Message { get; } = PlaceholderText;
}
