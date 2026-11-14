namespace GitHubBackup.App.ViewModels;

/// <summary>
/// One choice of a filter of the run history.
/// </summary>
/// <typeparam name="T">The type of the filter value.</typeparam>
/// <param name="Label">The text the list of choices shows.</param>
/// <param name="Value">The value of the filter, or <see langword="null"/> for the choice that does not filter.</param>
public sealed record HistoryFilterOption<T>(string Label, T Value);
