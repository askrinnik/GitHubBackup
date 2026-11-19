using System.IO.Abstractions;
using System.Text.Json;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.Infrastructure.Hosting;
using Microsoft.Extensions.Hosting;

namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Rewrites single keys of <see cref="GitHubBackupHostBuilder.AppSettingsFileName"/> in the content root and keeps
/// every other key and section of the file.
/// </summary>
/// <remarks>
/// <para>
/// The user edits the file by hand as well, so a write changes only the keys it is given: section and key names are
/// matched without regard to case, as the configuration matches them, a replaced key takes its canonical name, and
/// a key missing from the file is added. The file is rewritten as indented JSON: values survive, comments and the
/// original layout do not.
/// </para>
/// <para>
/// One read-change-write runs at a time. A file that exists but cannot be read, or whose text is not a JSON object,
/// is never written over: the write fails with the path of the file. A file that holds only white space counts as
/// no file.
/// </para>
/// </remarks>
/// <param name="fileSystem">The file system.</param>
/// <param name="environment">The host environment whose content root holds the file.</param>
/// <param name="writer">The writer that replaces the file atomically and keeps its previous version.</param>
internal sealed class AppSettingsFile(IFileSystem fileSystem, IHostEnvironment environment, AtomicFileWriter writer)
    : IDisposable
{
    /// <summary>Lets one read-change-write of the file run at a time.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Gets the absolute path of the file.</summary>
    public string FilePath => Path.Combine(environment.ContentRootPath, GitHubBackupHostBuilder.AppSettingsFileName);

    /// <summary>
    /// Sets keys of one section and keeps everything else in the file; creates the file when there is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The keys go into the first occurrence of the section whose value is an object, or, when there is none, into
    /// a new object that takes the place of the first occurrence; a section missing from the file is added at its
    /// end. In that occurrence the first property of each key is replaced in place and later ones are dropped; keys
    /// missing from it are appended.
    /// </para>
    /// <para>
    /// A key made by <see cref="AppSettingValue.Remove"/> is dropped from the section and not added.
    /// </para>
    /// <para>
    /// The keys are removed from every other object occurrence of the section, which the configuration would merge
    /// with the first one, so that no copy left there overrides the value written.
    /// </para>
    /// </remarks>
    /// <param name="sectionName">The canonical name of the section, for example <c>Tools</c>.</param>
    /// <param name="values">The keys with their new values.</param>
    /// <param name="cancellationToken">Cancels the write before the file is replaced.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="IOException">
    /// The file cannot be read, is not a JSON object or cannot be written; it is left as it is.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read or written; it is left as it is.</exception>
    public async Task WriteSectionValuesAsync(
        string sectionName, IReadOnlyList<AppSettingValue> values, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var document = await JsonSettingsText.ReadObjectAsync(fileSystem, FilePath, cancellationToken);
            var content = JsonSettingsText.Write(json => WriteRoot(json, document?.RootElement, sectionName, values));
            await writer.WriteAsync(FilePath, content, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    /// <summary>
    /// Writes the root object: the properties of <paramref name="root"/> with the section changed.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="root">The root object of the file, or <see langword="null"/> when there is no file.</param>
    /// <param name="sectionName">The canonical name of the section.</param>
    /// <param name="values">The keys with their new values.</param>
    private static void WriteRoot(
        Utf8JsonWriter json, JsonElement? root, string sectionName, IReadOnlyList<AppSettingValue> values)
    {
        var properties = root?.EnumerateObject().ToList() ?? [];
        var occurrences = properties
            .Select((property, index) => (property, index))
            .Where(item => IsNamed(item.property, sectionName))
            .ToList();
        var target = occurrences
            .Where(item => item.property.Value.ValueKind == JsonValueKind.Object)
            .Select(item => (int?)item.index)
            .FirstOrDefault() ?? occurrences.Select(item => (int?)item.index).FirstOrDefault();

        json.WriteStartObject();
        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            if (index == target)
            {
                json.WritePropertyName(sectionName);
                WriteTargetSection(json, property.Value, values);
            }
            else if (IsNamed(property, sectionName) && property.Value.ValueKind == JsonValueKind.Object)
            {
                json.WritePropertyName(property.Name);
                WriteWithout(json, property.Value, values);
            }
            else
            {
                property.WriteTo(json);
            }
        }

        if (target is null)
        {
            json.WritePropertyName(sectionName);
            WriteTargetSection(json, null, values);
        }

        json.WriteEndObject();
    }

    /// <summary>
    /// Writes the section that receives the keys.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="section">The section found in the file; <see langword="null"/> or a non-object value starts an empty one.</param>
    /// <param name="values">The keys with their new values.</param>
    private static void WriteTargetSection(Utf8JsonWriter json, JsonElement? section, IReadOnlyList<AppSettingValue> values)
    {
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        json.WriteStartObject();
        if (section is { ValueKind: JsonValueKind.Object } existing)
        {
            foreach (var property in existing.EnumerateObject())
            {
                if (FindValue(values, property.Name) is not { } value)
                {
                    property.WriteTo(json);
                }
                else if (written.Add(value.Key) && !value.IsRemoval)
                {
                    json.WritePropertyName(value.Key);
                    value.WriteValue(json);
                }
            }
        }

        foreach (var value in values.Where(value => !value.IsRemoval && !written.Contains(value.Key)))
        {
            json.WritePropertyName(value.Key);
            value.WriteValue(json);
        }

        json.WriteEndObject();
    }

    /// <summary>
    /// Writes a copy of another occurrence of the section without the keys being set.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="section">The object of the occurrence.</param>
    /// <param name="values">The keys being set.</param>
    private static void WriteWithout(Utf8JsonWriter json, JsonElement section, IReadOnlyList<AppSettingValue> values)
    {
        json.WriteStartObject();
        foreach (var property in section.EnumerateObject().Where(property => FindValue(values, property.Name) is null))
        {
            property.WriteTo(json);
        }

        json.WriteEndObject();
    }

    /// <summary>
    /// Returns the value of the key named <paramref name="name"/> without regard to case.
    /// </summary>
    /// <param name="values">The keys being set.</param>
    /// <param name="name">The name of a property in the file.</param>
    /// <returns>The key with its value, or <see langword="null"/> when the property is not one of them.</returns>
    private static AppSettingValue? FindValue(IReadOnlyList<AppSettingValue> values, string name) =>
        values.FirstOrDefault(value => string.Equals(value.Key, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns whether a property has the given name without regard to case.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <param name="name">The name.</param>
    /// <returns><see langword="true"/> when the names match.</returns>
    private static bool IsNamed(JsonProperty property, string name) =>
        string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase);
}
