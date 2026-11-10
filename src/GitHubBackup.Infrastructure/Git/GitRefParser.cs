using GitHubBackup.Core.Git;

namespace GitHubBackup.Infrastructure.Git;

/// <summary>
/// Turns the output of <c>git ls-remote</c> and <c>git for-each-ref</c> into a <see cref="RefSnapshot"/> and finds
/// the default branch in the output of <c>git ls-remote --symref</c>.
/// </summary>
/// <remarks>
/// Every parsed line has the form <c>&lt;object id&gt;&lt;TAB&gt;&lt;ref name&gt;</c>. Only branches and tags enter a
/// snapshot: pull request refs and other server refs do not change what a restore of the repository contains.
/// </remarks>
internal static class GitRefParser
{
    /// <summary>The full prefix of a branch on the server.</summary>
    private const string _serverBranchPrefix = "refs/heads/";

    /// <summary>The full prefix of a tag, on the server and in the clone.</summary>
    private const string _tagPrefix = "refs/tags/";

    /// <summary>The full prefix of a remote-tracking branch of <c>origin</c> in the clone.</summary>
    private const string _trackingBranchPrefix = "refs/remotes/origin/";

    /// <summary>The suffix git appends to a tag name on the line with the commit the tag points at.</summary>
    private const string _peeledSuffix = "^{}";

    /// <summary>The ref name of the symbolic ref that names the default branch.</summary>
    private const string _head = "HEAD";

    /// <summary>The prefix of a line of <c>ls-remote --symref</c> that names the target of a symbolic ref.</summary>
    private const string _symbolicRefPrefix = "ref: ";

    /// <summary>
    /// Builds the snapshot of the server from the output of <c>git ls-remote</c>.
    /// </summary>
    /// <param name="lines">The lines of standard output.</param>
    /// <returns>The branches under <c>refs/heads/</c> and the tags under <c>refs/tags/</c>, without peeled lines.</returns>
    public static RefSnapshot ParseRemoteRefs(IEnumerable<string> lines)
    {
        var refs = new List<KeyValuePair<string, string>>();
        foreach (var (objectId, name) in SplitLines(lines))
        {
            if (name.EndsWith(_peeledSuffix, StringComparison.Ordinal))
            {
                continue;
            }

            if (name.StartsWith(_serverBranchPrefix, StringComparison.Ordinal))
            {
                refs.Add(new(RefSnapshot.BranchPrefix + name[_serverBranchPrefix.Length..], objectId));
            }
            else if (name.StartsWith(_tagPrefix, StringComparison.Ordinal))
            {
                refs.Add(new(RefSnapshot.TagPrefix + name[_tagPrefix.Length..], objectId));
            }
        }

        return new(refs);
    }

    /// <summary>
    /// Builds the snapshot of the clone from the output of <c>git for-each-ref</c>.
    /// </summary>
    /// <param name="lines">The lines of standard output.</param>
    /// <returns>
    /// The remote-tracking branches of <c>origin</c> as <c>heads/&lt;name&gt;</c>, except <c>origin/HEAD</c>, and the
    /// tags.
    /// </returns>
    public static RefSnapshot ParseLocalRefs(IEnumerable<string> lines)
    {
        var refs = new List<KeyValuePair<string, string>>();
        foreach (var (objectId, name) in SplitLines(lines))
        {
            if (name.StartsWith(_trackingBranchPrefix, StringComparison.Ordinal))
            {
                var branch = name[_trackingBranchPrefix.Length..];

                // origin/HEAD mirrors the server's HEAD symbolic ref, which is not a branch of its own.
                if (!string.Equals(branch, _head, StringComparison.Ordinal))
                {
                    refs.Add(new(RefSnapshot.BranchPrefix + branch, objectId));
                }
            }
            else if (name.StartsWith(_tagPrefix, StringComparison.Ordinal))
            {
                refs.Add(new(RefSnapshot.TagPrefix + name[_tagPrefix.Length..], objectId));
            }
        }

        return new(refs);
    }

    /// <summary>
    /// Finds the default branch in the output of <c>git ls-remote --symref origin HEAD</c>.
    /// </summary>
    /// <param name="lines">The lines of standard output.</param>
    /// <returns>
    /// The branch <c>HEAD</c> points at, or <see langword="null"/> when <c>HEAD</c> names no branch or does not resolve
    /// to a commit, as on a server without any branch.
    /// </returns>
    public static string? ParseDefaultBranch(IEnumerable<string> lines)
    {
        string? branch = null;
        var resolves = false;
        foreach (var line in lines)
        {
            var separator = line.IndexOf('\t', StringComparison.Ordinal);
            if (separator < 0 || !string.Equals(line[(separator + 1)..], _head, StringComparison.Ordinal))
            {
                continue;
            }

            var value = line[..separator];
            if (!value.StartsWith(_symbolicRefPrefix, StringComparison.Ordinal))
            {
                resolves = value.Length > 0;
                continue;
            }

            var target = value[_symbolicRefPrefix.Length..];
            if (target.Length > _serverBranchPrefix.Length
                && target.StartsWith(_serverBranchPrefix, StringComparison.Ordinal))
            {
                branch = target[_serverBranchPrefix.Length..];
            }
        }

        // A server may advertise the target of an unborn HEAD; without the id line HEAD names no existing branch.
        return resolves ? branch : null;
    }

    /// <summary>
    /// Splits each line at its first tab into the object id and the ref name, skipping lines without both.
    /// </summary>
    /// <param name="lines">The lines of standard output.</param>
    /// <returns>The object id and ref name per line.</returns>
    private static IEnumerable<(string ObjectId, string Name)> SplitLines(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var separator = line.IndexOf('\t', StringComparison.Ordinal);
            if (separator > 0 && separator < line.Length - 1)
            {
                yield return (line[..separator], line[(separator + 1)..]);
            }
        }
    }
}
