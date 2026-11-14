namespace GitHubBackup.App.ViewModels;

/// <summary>
/// A field of the settings editor that shows the validation errors of one location in <c>backup-config.json</c>.
/// </summary>
/// <param name="Form">The form that holds the field.</param>
/// <param name="PropertyName">The name of the property bound to the field.</param>
internal sealed record SettingsField(SettingsFormViewModel Form, string PropertyName);
