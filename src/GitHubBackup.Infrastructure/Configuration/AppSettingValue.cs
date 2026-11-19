using System.Text.Json;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// A key of a section of <c>appsettings.json</c> with the value <see cref="AppSettingsFile"/> writes for it.
/// </summary>
/// <param name="Key">The canonical name of the key, as the options class names it.</param>
/// <param name="WriteValue">Writes the value of the key, after its property name.</param>
internal sealed record AppSettingValue(string Key, Action<Utf8JsonWriter> WriteValue)
{
    /// <summary>
    /// Gets whether the key is removed from the file rather than written; <see cref="WriteValue"/> is then not used.
    /// </summary>
    public bool IsRemoval { get; init; }

    /// <summary>
    /// Creates a key that is removed from the section.
    /// </summary>
    /// <param name="key">The canonical name of the key.</param>
    /// <returns>The key to remove.</returns>
    public static AppSettingValue Remove(string key) => new(key, _ => { }) { IsRemoval = true };

    /// <summary>
    /// Creates a key whose value is a text, or <c>null</c> when it is not set.
    /// </summary>
    /// <param name="key">The canonical name of the key.</param>
    /// <param name="value">The text, or <see langword="null"/>.</param>
    /// <returns>The key with its value.</returns>
    public static AppSettingValue FromText(string key, string? value) => new(
        key,
        json =>
        {
            if (value is null)
            {
                json.WriteNullValue();
            }
            else
            {
                json.WriteStringValue(value);
            }
        });

    /// <summary>
    /// Creates a key whose value is a boolean.
    /// </summary>
    /// <param name="key">The canonical name of the key.</param>
    /// <param name="value">The value.</param>
    /// <returns>The key with its value.</returns>
    public static AppSettingValue FromFlag(string key, bool value) => new(key, json => json.WriteBooleanValue(value));
}
