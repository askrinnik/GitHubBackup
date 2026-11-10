using System.Text;

namespace GitHubBackup.Core.Git;

/// <summary>
/// Maps the branches and tags of a repository to the object ids they point at, under the names
/// <c>heads/&lt;name&gt;</c> and <c>tags/&lt;name&gt;</c>.
/// </summary>
/// <remarks>
/// The server snapshot and the clone snapshot use the same names, so two snapshots of one state are equal. An
/// annotated tag maps to the id of the tag object, not of the commit it points at. Names compare ordinally, and
/// equality compares the content regardless of order.
/// </remarks>
public sealed class RefSnapshot : IEquatable<RefSnapshot>
{
    /// <summary>The prefix of a branch name in the snapshot.</summary>
    public const string BranchPrefix = "heads/";

    /// <summary>The prefix of a tag name in the snapshot.</summary>
    public const string TagPrefix = "tags/";

    /// <summary>The object id per ref name.</summary>
    private readonly Dictionary<string, string> _refs = new(StringComparer.Ordinal);

    /// <summary>
    /// Creates a snapshot of the given refs.
    /// </summary>
    /// <param name="refs">The object id per ref name.</param>
    /// <exception cref="ArgumentException">
    /// A name does not start with <see cref="BranchPrefix"/> or <see cref="TagPrefix"/> followed by a name, an object
    /// id is empty, or a name occurs twice.
    /// </exception>
    public RefSnapshot(IEnumerable<KeyValuePair<string, string>> refs)
    {
        foreach (var (name, objectId) in refs)
        {
            if (!HasNameAfter(name, BranchPrefix) && !HasNameAfter(name, TagPrefix))
            {
                throw new ArgumentException(
                    $"The ref '{name}' must start with '{BranchPrefix}' or '{TagPrefix}' followed by a name.",
                    nameof(refs));
            }

            if (string.IsNullOrWhiteSpace(objectId))
            {
                throw new ArgumentException($"The ref '{name}' has no object id.", nameof(refs));
            }

            if (!_refs.TryAdd(name, objectId))
            {
                throw new ArgumentException($"The ref '{name}' occurs more than once.", nameof(refs));
            }
        }
    }

    /// <summary>Gets a snapshot without any branch or tag.</summary>
    public static RefSnapshot Empty { get; } = new([]);

    /// <summary>Gets the object id per ref name.</summary>
    public IReadOnlyDictionary<string, string> Refs => _refs;

    /// <summary>Gets a value indicating whether the snapshot holds at least one branch.</summary>
    public bool HasBranches => _refs.Keys.Any(name => name.StartsWith(BranchPrefix, StringComparison.Ordinal));

    /// <summary>
    /// Returns whether the snapshot holds the branch <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The branch name without a prefix, such as <c>main</c>.</param>
    /// <returns><see langword="true"/> when <c>heads/<paramref name="name"/></c> is in the snapshot.</returns>
    public bool ContainsBranch(string name) => _refs.ContainsKey(BranchPrefix + name);

    /// <inheritdoc />
    public bool Equals(RefSnapshot? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return _refs.Count == other._refs.Count
            && _refs.All(entry => other._refs.TryGetValue(entry.Key, out var objectId)
                && string.Equals(entry.Value, objectId, StringComparison.Ordinal));
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RefSnapshot);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        // XOR keeps the hash independent of the order of the entries, matching Equals.
        var hash = 0;
        foreach (var (name, objectId) in _refs)
        {
            hash ^= HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(name), StringComparer.Ordinal.GetHashCode(objectId));
        }

        return hash;
    }

    /// <summary>
    /// Returns the refs sorted by name, one <c>name=id</c> pair per entry, for logs and test failures.
    /// </summary>
    /// <returns>The text of the snapshot.</returns>
    public override string ToString()
    {
        var builder = new StringBuilder("{");
        foreach (var (name, objectId) in _refs.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            builder.Append(builder.Length > 1 ? ", " : " ").Append(name).Append('=').Append(objectId);
        }

        return builder.Append(_refs.Count > 0 ? " }" : "}").ToString();
    }

    /// <summary>
    /// Returns whether <paramref name="name"/> starts with <paramref name="prefix"/> and continues after it.
    /// </summary>
    /// <param name="name">The ref name.</param>
    /// <param name="prefix">The expected prefix.</param>
    /// <returns><see langword="true"/> when a non-empty name follows the prefix.</returns>
    private static bool HasNameAfter(string name, string prefix) =>
        name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal);
}
