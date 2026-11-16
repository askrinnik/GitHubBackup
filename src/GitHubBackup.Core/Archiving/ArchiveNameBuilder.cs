using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.GitHub;
using GitHubBackup.Core.State;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Core.Archiving;

/// <summary>
/// Implements <see cref="IArchiveNameBuilder"/> with the local time of the injected clock.
/// </summary>
/// <param name="timeProvider">The clock; its local time enters the name.</param>
/// <param name="options">Supplies the length of the hash in the name.</param>
public sealed class ArchiveNameBuilder(TimeProvider timeProvider, IOptions<BackupOptions> options) : IArchiveNameBuilder
{
    /// <summary>The format of the time in the name.</summary>
    private const string _timeFormat = "yyyyMMdd-HHmmss";

    /// <summary>The extension of an archive file.</summary>
    private const string _extension = ".zip";

    /// <summary>The text between the repository name and the time in the name of a wiki archive.</summary>
    private const string _wikiSeparator = ".wiki_";

    /// <summary>The position of the dash between the date and the time in <see cref="_timeFormat"/>.</summary>
    private const int _timeDashIndex = 8;

    /// <summary>The characters of a hash in a name, in either case.</summary>
    private static readonly SearchValues<char> _hexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    /// <inheritdoc />
    public ArchiveName Build(string owner, string repository, BackupTarget target, string headCommit)
    {
        if (!GitHubUrl.IsValidLogin(owner))
        {
            throw new ArgumentException($"'{owner}' is not a valid GitHub login.", nameof(owner));
        }

        if (!GitHubUrl.IsValidRepositoryName(repository))
        {
            throw new ArgumentException($"'{repository}' is not a valid GitHub repository name.", nameof(repository));
        }

        var length = options.Value.ShortHashLength;
        if (headCommit is null || headCommit.Length < length || !headCommit.All(char.IsAsciiHexDigit))
        {
            throw new ArgumentException(
                $"The commit id must be hexadecimal with at least {length} characters.", nameof(headCommit));
        }

        var createdAt = timeProvider.GetLocalNow();
        var suffix = target == BackupTarget.Wiki ? ".wiki" : string.Empty;
        var time = createdAt.ToString(_timeFormat, CultureInfo.InvariantCulture);
        var hash = headCommit[..length].ToLowerInvariant();
        return new ArchiveName($"{owner}_{repository}{suffix}_{time}_{hash}.zip", createdAt);
    }

    /// <inheritdoc />
    public bool TryParse(
        string fileName, string owner, string repository, [NotNullWhen(true)] out ParsedArchiveName? parsed)
    {
        parsed = null;
        var rest = fileName.AsSpan();
        if (owner.Length == 0 || repository.Length == 0 || !rest.EndsWith(_extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        rest = rest[..^_extension.Length];
        if (!rest.StartsWith(owner, StringComparison.OrdinalIgnoreCase)
            || rest.Length <= owner.Length
            || rest[owner.Length] != '_')
        {
            return false;
        }

        rest = rest[(owner.Length + 1)..];
        if (!rest.StartsWith(repository, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        rest = rest[repository.Length..];
        BackupTarget target;
        if (rest.StartsWith("_", StringComparison.Ordinal))
        {
            target = BackupTarget.Repository;
            rest = rest[1..];
        }
        else if (rest.StartsWith(_wikiSeparator, StringComparison.OrdinalIgnoreCase))
        {
            target = BackupTarget.Wiki;
            rest = rest[_wikiSeparator.Length..];
        }
        else
        {
            return false;
        }

        var hashLength = options.Value.ShortHashLength;
        if (rest.Length != _timeFormat.Length + 1 + hashLength || rest[_timeFormat.Length] != '_')
        {
            return false;
        }

        var time = rest[.._timeFormat.Length];
        var hash = rest[(_timeFormat.Length + 1)..];
        if (!IsTime(time) || hash.ContainsAnyExcept(_hexDigits)
            || !DateTime.TryParseExact(time, _timeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return false;
        }

        // The name holds the local wall-clock time without an offset; the offset the local time zone had at that time
        // restores the instant, as the time zone has not changed since the archive was created in the usual case.
        var offset = timeProvider.LocalTimeZone.GetUtcOffset(local);
        parsed = new(target, new DateTimeOffset(local, offset), hash.ToString().ToLowerInvariant());
        return true;
    }

    /// <summary>
    /// Returns whether <paramref name="time"/> has the shape of <see cref="_timeFormat"/>: digits with a dash between
    /// the date and the time.
    /// </summary>
    /// <param name="time">The characters of the time; exactly as long as <see cref="_timeFormat"/>.</param>
    /// <returns><see langword="true"/> when every character is in place.</returns>
    private static bool IsTime(ReadOnlySpan<char> time)
    {
        for (var index = 0; index < time.Length; index++)
        {
            var valid = index == _timeDashIndex ? time[index] == '-' : char.IsAsciiDigit(time[index]);
            if (!valid)
            {
                return false;
            }
        }

        return true;
    }
}
