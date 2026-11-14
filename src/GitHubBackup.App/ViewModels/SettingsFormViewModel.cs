using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using GitHubBackup.Core.BackupConfiguration;

namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Shows the errors of the settings editor at its fields: immediate hints about the form of a value, and the errors
/// that the full validation reports when the settings are saved.
/// </summary>
/// <remarks>
/// A reported error stays at its field only while the field holds the value it was reported for, so editing the field
/// clears it. Each field declares one of the static <c>Validate…</c> methods through
/// <see cref="CustomValidationAttribute"/>; the reported error takes the place of the immediate hint, so a field never
/// shows the same problem twice.
/// </remarks>
public abstract class SettingsFormViewModel : ObservableValidator
{
    /// <summary>The hint shown at a required path that is empty.</summary>
    public const string RequiredPathMessage = "Enter an absolute path, such as C:\\Backups.";

    /// <summary>The hint shown at a path that is set but not absolute.</summary>
    public const string AbsolutePathMessage = "The path must be absolute, such as C:\\Backups.";

    /// <summary>The hint shown at a branch that cannot be passed to git.</summary>
    public const string BranchMessage = "The branch must not start with '-'; leave it empty for the default branch.";

    /// <summary>The errors reported by the full validation, by property name.</summary>
    private readonly Dictionary<string, ReportedError> _reportedErrors = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the error reported for the property under validation, or the immediate hint when there is none.
    /// </summary>
    /// <param name="value">The value of the property.</param>
    /// <param name="context">The context that names the form and the property.</param>
    /// <returns>The error, or <see cref="ValidationResult.Success"/>.</returns>
    public static ValidationResult? ValidateText(string? value, ValidationContext context) =>
        Reported(value, context) ?? ValidationResult.Success;

    /// <summary>
    /// Validates a path that must be set and absolute.
    /// </summary>
    /// <param name="value">The value of the property.</param>
    /// <param name="context">The context that names the form and the property.</param>
    /// <returns>The error, or <see cref="ValidationResult.Success"/>.</returns>
    public static ValidationResult? ValidateRequiredPath(string? value, ValidationContext context) =>
        Reported(value, context)
        ?? (string.IsNullOrWhiteSpace(value) ? Error(RequiredPathMessage, context) : CheckAbsolute(value, context));

    /// <summary>
    /// Validates a path that may be empty, and is absolute when set.
    /// </summary>
    /// <param name="value">The value of the property.</param>
    /// <param name="context">The context that names the form and the property.</param>
    /// <returns>The error, or <see cref="ValidationResult.Success"/>.</returns>
    public static ValidationResult? ValidateOptionalPath(string? value, ValidationContext context) =>
        Reported(value, context)
        ?? (string.IsNullOrWhiteSpace(value) ? ValidationResult.Success : CheckAbsolute(value, context));

    /// <summary>
    /// Validates a branch that may be empty for the default branch.
    /// </summary>
    /// <param name="value">The value of the property.</param>
    /// <param name="context">The context that names the form and the property.</param>
    /// <returns>The error, or <see cref="ValidationResult.Success"/>.</returns>
    public static ValidationResult? ValidateBranch(string? value, ValidationContext context) =>
        Reported(value, context)
        ?? (string.IsNullOrWhiteSpace(value) || BackupConfigValidator.IsValidBranch(value.Trim())
            ? ValidationResult.Success
            : Error(BranchMessage, context));

    /// <summary>
    /// Shows <paramref name="message"/> at <paramref name="propertyName"/> until the property changes.
    /// </summary>
    /// <param name="propertyName">The name of the property.</param>
    /// <param name="message">The error message.</param>
    internal void ReportError(string propertyName, string message)
    {
        if (_reportedErrors.TryGetValue(propertyName, out var existing))
        {
            existing.Messages.Add(message);
            return;
        }

        var value = GetType().GetProperty(propertyName)?.GetValue(this);
        _reportedErrors[propertyName] = new(value, [message]);
    }

    /// <summary>
    /// Removes every reported error; <see cref="RefreshErrors"/> shows the change.
    /// </summary>
    internal void ClearReportedErrors() => _reportedErrors.Clear();

    /// <summary>
    /// Removes the reported error of one property; <see cref="RefreshError"/> shows the change.
    /// </summary>
    /// <param name="propertyName">The name of the property.</param>
    internal void ClearReportedError(string propertyName) => _reportedErrors.Remove(propertyName);

    /// <summary>
    /// Validates every field again, so the fields show the reported errors and the immediate hints.
    /// </summary>
    internal void RefreshErrors() => ValidateAllProperties();

    /// <summary>
    /// Validates one field again.
    /// </summary>
    /// <param name="propertyName">The name of the property bound to the field.</param>
    internal void RefreshError(string propertyName) =>
        ValidateProperty(GetType().GetProperty(propertyName)?.GetValue(this), propertyName);

    /// <summary>
    /// Returns the value of a text field without surrounding white space, or <see langword="null"/> when it is empty.
    /// </summary>
    /// <param name="value">The text of the field.</param>
    /// <returns>The trimmed value, or <see langword="null"/>.</returns>
    protected static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Splits the text of an exclusion field into masks.
    /// </summary>
    /// <remarks>
    /// A comma never appears in a GitHub owner or repository name, so it separates the masks safely.
    /// </remarks>
    /// <param name="text">The masks separated by commas.</param>
    /// <returns>The masks without surrounding white space, without empty entries.</returns>
    protected static List<string> ParseMasks(string? text) =>
        [.. (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    /// <summary>
    /// Joins exclusion masks into the text of an exclusion field.
    /// </summary>
    /// <param name="masks">The masks.</param>
    /// <returns>The masks separated by commas.</returns>
    protected static string FormatMasks(IEnumerable<string> masks) => string.Join(", ", masks);

    /// <summary>
    /// Returns the error reported for the property under validation while it still holds the reported value.
    /// </summary>
    /// <param name="value">The current value of the property.</param>
    /// <param name="context">The context that names the form and the property.</param>
    /// <returns>The error, or <see langword="null"/> when none applies.</returns>
    private static ValidationResult? Reported(string? value, ValidationContext context) =>
        context.ObjectInstance is SettingsFormViewModel form
        && context.MemberName is { } name
        && form._reportedErrors.TryGetValue(name, out var error)
        && Equals(error.Value, value)
            ? Error(string.Join(" ", error.Messages), context)
            : null;

    /// <summary>
    /// Returns the hint for a path that is set but not absolute.
    /// </summary>
    /// <param name="value">The path, not empty.</param>
    /// <param name="context">The context that names the property.</param>
    /// <returns>The error, or <see cref="ValidationResult.Success"/>.</returns>
    private static ValidationResult? CheckAbsolute(string value, ValidationContext context) =>
        BackupConfigValidator.IsAbsolutePath(value.Trim()) ? ValidationResult.Success : Error(AbsolutePathMessage, context);

    /// <summary>
    /// Creates an error for the property under validation.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="context">The context that names the property.</param>
    /// <returns>The error.</returns>
    private static ValidationResult Error(string message, ValidationContext context) =>
        new(message, context.MemberName is { } name ? [name] : []);

    /// <summary>
    /// An error reported for a property together with the value the property held then.
    /// </summary>
    /// <param name="Value">The value of the property when the error was reported.</param>
    /// <param name="Messages">The error messages.</param>
    private sealed record ReportedError(object? Value, List<string> Messages);
}
