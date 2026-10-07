using System.Reflection;

namespace GitHubBackup.ArchitectureTests;

/// <summary>
/// Finds the direct assembly references that match a list of forbidden assembly names.
/// </summary>
public static class ForbiddenReferenceFinder
{
    /// <summary>
    /// Returns the names of the assemblies directly referenced by <paramref name="assembly" /> that match a forbidden name.
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <param name="forbiddenNames">Assembly names; a reference matches by exact name or as a <c>name.</c> prefix.</param>
    /// <returns>The matching referenced assembly names.</returns>
    public static IReadOnlyList<string> Find(Assembly assembly, IEnumerable<string> forbiddenNames) =>
        Find(assembly.GetReferencedAssemblies(), forbiddenNames);

    /// <summary>
    /// Returns the names of the <paramref name="references" /> that match a forbidden name.
    /// </summary>
    /// <param name="references">The referenced assemblies to inspect.</param>
    /// <param name="forbiddenNames">Assembly names; a reference matches by exact name or as a <c>name.</c> prefix.</param>
    /// <returns>The matching referenced assembly names.</returns>
    public static IReadOnlyList<string> Find(IEnumerable<AssemblyName> references, IEnumerable<string> forbiddenNames)
    {
        var forbidden = forbiddenNames.ToArray();

        return [.. references
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => forbidden.Any(forbiddenName => Matches(name, forbiddenName)))];
    }

    private static bool Matches(string name, string forbiddenName) =>
        name.Equals(forbiddenName, StringComparison.OrdinalIgnoreCase)
        || name.StartsWith(forbiddenName + ".", StringComparison.OrdinalIgnoreCase);
}
