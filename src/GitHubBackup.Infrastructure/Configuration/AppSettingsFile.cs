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
    /// <remarks>Behaves as <see cref="WriteSectionsAsync"/> with this one section.</remarks>
    /// <param name="sectionPath">The canonical path of the section, for example <c>Tools</c>.</param>
    /// <param name="values">The keys with their new values.</param>
    /// <param name="cancellationToken">Cancels the write before the file is replaced.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="ArgumentException"><paramref name="sectionPath"/> has an empty name.</exception>
    /// <exception cref="IOException">
    /// The file cannot be read, is not a JSON object or cannot be written; it is left as it is.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read or written; it is left as it is.</exception>
    public Task WriteSectionValuesAsync(
        string sectionPath, IReadOnlyList<AppSettingValue> values, CancellationToken cancellationToken) =>
        WriteSectionsAsync([new AppSettingsSection(sectionPath, values)], cancellationToken);

    /// <summary>
    /// Sets keys of several sections in one write and keeps everything else in the file; creates the file when there
    /// is none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A section path names nested sections separated by <c>:</c>. At each level the keys go into the first
    /// occurrence of the section whose value is an object, or, when there is none, into a new object that takes the
    /// place of the first occurrence; a section missing from its parent is added at the parent's end, together with
    /// the missing sections above it. In that occurrence the first property of each key is replaced in place and
    /// later ones are dropped; keys missing from it are appended.
    /// </para>
    /// <para>
    /// A key made by <see cref="AppSettingValue.Remove"/> is dropped from the section and not added.
    /// </para>
    /// <para>
    /// The keys are removed from every other object occurrence of the section, which the configuration would merge
    /// with the first one, so that no copy left there overrides the value written.
    /// </para>
    /// <para>
    /// The file is replaced once, so it keeps a single backup of its previous version.
    /// </para>
    /// </remarks>
    /// <param name="sections">The sections with the keys to set.</param>
    /// <param name="cancellationToken">Cancels the write before the file is replaced.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="ArgumentException">A section path has an empty name.</exception>
    /// <exception cref="IOException">
    /// The file cannot be read, is not a JSON object or cannot be written; it is left as it is.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The file cannot be read or written; it is left as it is.</exception>
    public async Task WriteSectionsAsync(IReadOnlyList<AppSettingsSection> sections, CancellationToken cancellationToken)
    {
        var changes = SectionChange.Build(sections);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            using var document = await JsonSettingsText.ReadObjectAsync(fileSystem, FilePath, cancellationToken);
            var content = JsonSettingsText.Write(json => WriteChanged(json, document?.RootElement, changes));
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
    /// Writes an object that receives changes: the properties of <paramref name="existing"/> with the keys and the
    /// child sections of <paramref name="change"/> set.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="existing">
    /// The object found in the file; <see langword="null"/> or a non-object value starts an empty one.
    /// </param>
    /// <param name="change">The keys and child sections to set.</param>
    private static void WriteChanged(Utf8JsonWriter json, JsonElement? existing, SectionChange change)
    {
        var properties = existing is { ValueKind: JsonValueKind.Object } found ? found.EnumerateObject().ToList() : [];
        var targets = new Dictionary<int, SectionChange>();
        foreach (var child in change.Children)
        {
            var occurrences = properties
                .Select((property, index) => (property, index))
                .Where(item => IsNamed(item.property, child.Name))
                .ToList();
            var target = occurrences
                .Where(item => item.property.Value.ValueKind == JsonValueKind.Object)
                .Select(item => (int?)item.index)
                .FirstOrDefault() ?? occurrences.Select(item => (int?)item.index).FirstOrDefault();
            if (target is { } index)
            {
                targets[index] = child;
            }
        }

        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        json.WriteStartObject();
        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            if (targets.TryGetValue(index, out var child))
            {
                json.WritePropertyName(child.Name);
                WriteChanged(json, property.Value, child);
            }
            else if (change.FindChild(property.Name) is { } other)
            {
                WriteOtherOccurrence(json, property, other);
            }
            else if (FindValue(change.Values, property.Name) is not { } value)
            {
                property.WriteTo(json);
            }
            else if (written.Add(value.Key) && !value.IsRemoval)
            {
                json.WritePropertyName(value.Key);
                value.WriteValue(json);
            }
        }

        foreach (var value in change.Values.Where(value => !value.IsRemoval && written.Add(value.Key)))
        {
            json.WritePropertyName(value.Key);
            value.WriteValue(json);
        }

        foreach (var child in change.Children.Where(child => !targets.ContainsValue(child)))
        {
            json.WritePropertyName(child.Name);
            WriteChanged(json, null, child);
        }

        json.WriteEndObject();
    }

    /// <summary>
    /// Writes an occurrence of a section that does not receive the changes: an object without the keys being set, at
    /// every level, and any other value as it is.
    /// </summary>
    /// <param name="json">The writer.</param>
    /// <param name="property">The occurrence.</param>
    /// <param name="change">The keys and child sections being set in the section.</param>
    private static void WriteOtherOccurrence(Utf8JsonWriter json, JsonProperty property, SectionChange change)
    {
        if (property.Value.ValueKind != JsonValueKind.Object)
        {
            property.WriteTo(json);
            return;
        }

        json.WritePropertyName(property.Name);
        json.WriteStartObject();
        foreach (var inner in property.Value.EnumerateObject().Where(inner => FindValue(change.Values, inner.Name) is null))
        {
            if (change.FindChild(inner.Name) is { } child)
            {
                WriteOtherOccurrence(json, inner, child);
            }
            else
            {
                inner.WriteTo(json);
            }
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

    /// <summary>
    /// The changes to one object of the file: the keys set in it and the child sections that change.
    /// </summary>
    /// <param name="name">The canonical name of the section; empty for the root object.</param>
    private sealed class SectionChange(string name)
    {
        /// <summary>Gets the canonical name of the section.</summary>
        public string Name { get; } = name;

        /// <summary>Gets the keys set in the section, in the order given.</summary>
        public List<AppSettingValue> Values { get; } = [];

        /// <summary>Gets the child sections that change, in the order first named.</summary>
        public List<SectionChange> Children { get; } = [];

        /// <summary>
        /// Builds the changes of the root object from the sections to set.
        /// </summary>
        /// <param name="sections">The sections with the keys to set.</param>
        /// <returns>The changes of the root object.</returns>
        /// <exception cref="ArgumentException">A section path has an empty name.</exception>
        public static SectionChange Build(IReadOnlyList<AppSettingsSection> sections)
        {
            var root = new SectionChange(string.Empty);
            foreach (var section in sections)
            {
                var names = section.Path.Split(':');
                if (names.Any(string.IsNullOrWhiteSpace))
                {
                    throw new ArgumentException($"The section path '{section.Path}' has an empty name.", nameof(sections));
                }

                var node = root;
                foreach (var name in names)
                {
                    node = node.FindChild(name) ?? node.AddChild(name);
                }

                node.Values.AddRange(section.Values);
            }

            return root;
        }

        /// <summary>
        /// Returns the child section named <paramref name="name"/> without regard to case.
        /// </summary>
        /// <param name="name">The name of a property in the file or of a section in a path.</param>
        /// <returns>The child section, or <see langword="null"/> when it does not change.</returns>
        public SectionChange? FindChild(string name) =>
            Children.FirstOrDefault(child => string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Adds a child section that changes.
        /// </summary>
        /// <param name="name">The canonical name of the child section.</param>
        /// <returns>The added child section.</returns>
        private SectionChange AddChild(string name)
        {
            var child = new SectionChange(name);
            Children.Add(child);
            return child;
        }
    }
}
