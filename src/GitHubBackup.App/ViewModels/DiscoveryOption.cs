namespace GitHubBackup.App.ViewModels;

/// <summary>
/// One entry of a choice list in the discovery dialog: the value a row stores and the text the list shows.
/// </summary>
/// <typeparam name="T">The type of the value.</typeparam>
/// <param name="Value">The value.</param>
/// <param name="Label">The text shown for the value.</param>
public sealed record DiscoveryOption<T>(T Value, string Label);
