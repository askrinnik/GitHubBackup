using System.Buffers;
using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Validates a <see cref="BackupConfig"/>: absolute paths, GitHub URLs, repository names and identifiers, unique
/// accounts and repositories, distinct archive folders, distinct clone folders, and clones kept out of the archive
/// tree.
/// </summary>
/// <remarks>
/// <para>
/// An entry whose URL, name or path is already reported as invalid is left out of the duplicate and archive folder
/// checks, so one mistake produces one error.
/// </para>
/// <para>
/// Every path is a Windows path: an absolute path starts with a drive letter or a UNC prefix, the separator is
/// <c>\</c>, the characters Windows forbids are rejected, and paths are compared without regard to case. The checks
/// rely on <see cref="Path"/> behaving as it does on Windows, the only operating system the application runs on.
/// </para>
/// </remarks>
public sealed class BackupConfigValidator : IBackupConfigValidator
{
    /// <summary>The characters that can never appear in a Windows path: control characters and <c>&lt;&gt;"|?*</c>.</summary>
    private static readonly SearchValues<char> _invalidPathCharacters = SearchValues.Create(
        [.. Enumerable.Range(0, 32).Select(code => (char)code), '<', '>', '"', '|', '?', '*']);

    /// <inheritdoc />
    public BackupConfigValidationResult Validate(BackupConfig config, string defaultSourcesRoot)
    {
        if (config.SchemaVersion != BackupConfig.CurrentSchemaVersion)
        {
            // The rest of a file in another format cannot be interpreted, so its errors would only mislead.
            return new(
                [$"schemaVersion {config.SchemaVersion} is not supported; the supported version is {BackupConfig.CurrentSchemaVersion}."],
                []);
        }

        var pass = new ValidationPass(config);
        pass.Run();
        pass.CheckSourcesRoot(config.SourcesRoot ?? defaultSourcesRoot);
        pass.CheckClonePaths(config.SourcesRoot ?? defaultSourcesRoot);
        return new(pass.Errors, pass.Warnings);
    }

    /// <summary>
    /// Returns whether <paramref name="path"/> is a fully qualified Windows path without invalid characters.
    /// </summary>
    /// <remarks>
    /// Device-namespace paths (<c>\\.\</c>, <c>\\?\</c>) are rejected: they bypass path normalization and can name
    /// devices instead of folders.
    /// </remarks>
    /// <param name="path">The path to check.</param>
    /// <returns><see langword="true"/> for paths such as <c>C:\Backups</c> or <c>\\server\share</c>.</returns>
    public static bool IsAbsolutePath(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && !path.AsSpan().ContainsAny(_invalidPathCharacters)
        && path.LastIndexOf(':') is -1 or 1
        && !IsDeviceNamespacePath(path)
        && Path.IsPathFullyQualified(path);

    /// <summary>
    /// Returns whether <paramref name="branch"/> can be stored as the branch of a repository entry.
    /// </summary>
    /// <remarks>A branch is passed to git, where a leading <c>-</c> would be read as an option.</remarks>
    /// <param name="branch">The branch name to check.</param>
    /// <returns><see langword="true"/> when the name is not blank and does not start with <c>-</c>.</returns>
    public static bool IsValidBranch(string branch) =>
        !string.IsNullOrWhiteSpace(branch) && !branch.StartsWith('-');

    /// <summary>
    /// Returns whether <paramref name="path"/> starts with <c>\\.\</c> or <c>\\?\</c>, with either separator.
    /// </summary>
    /// <param name="path">The path to check.</param>
    /// <returns><see langword="true"/> for a device-namespace path.</returns>
    private static bool IsDeviceNamespacePath(string path) =>
        path.Length >= 4
        && path[0] is '\\' or '/'
        && path[1] is '\\' or '/'
        && path[2] is '.' or '?'
        && path[3] is '\\' or '/';

    /// <summary>
    /// Returns <paramref name="path"/> in the form used to compare folders: full, without a trailing separator.
    /// </summary>
    /// <param name="path">An absolute path.</param>
    /// <returns>The normalized path.</returns>
    private static string Normalize(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>
    /// Returns whether <paramref name="path"/> is <paramref name="folder"/> or lies inside it.
    /// </summary>
    /// <param name="path">An absolute path.</param>
    /// <param name="folder">An absolute folder.</param>
    /// <returns><see langword="true"/> when the path is the folder or one of its descendants.</returns>
    private static bool IsSameOrInside(string path, string folder)
    {
        var normalizedPath = Normalize(path);
        var normalizedFolder = Normalize(folder);
        return normalizedPath.Equals(normalizedFolder, StringComparison.OrdinalIgnoreCase)
            || normalizedPath.StartsWith(normalizedFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns whether one of two folders is the other or lies inside it.
    /// </summary>
    /// <param name="first">An absolute folder.</param>
    /// <param name="second">Another absolute folder.</param>
    /// <returns><see langword="true"/> when the folders are the same or nested.</returns>
    private static bool Overlaps(string first, string second) =>
        IsSameOrInside(first, second) || IsSameOrInside(second, first);

    /// <summary>
    /// Returns whether <paramref name="path"/> is the root of a drive or of a network share.
    /// </summary>
    /// <param name="path">An absolute path.</param>
    /// <returns><see langword="true"/> for paths such as <c>D:\</c> or <c>\\server\share</c>.</returns>
    private static bool IsRoot(string path)
    {
        var full = Normalize(path);
        var root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(full, root, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Collects the errors and warnings of one configuration.
    /// </summary>
    /// <param name="config">The configuration being validated.</param>
    private sealed class ValidationPass(BackupConfig config)
    {
        /// <summary>The first location of each account login.</summary>
        private readonly Dictionary<string, string> _accounts = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The first location of each repository ID.</summary>
        private readonly Dictionary<long, string> _ids = [];

        /// <summary>The first location of each <c>owner/name</c>.</summary>
        private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>The resolved archive folder of every repository whose inputs are valid, with its location.</summary>
        private readonly List<(string Location, string Folder)> _folders = [];

        /// <summary>The valid <c>clonePath</c> of every repository that sets one, with the location of the entry.</summary>
        private readonly List<(string Location, string ClonePath)> _clonePaths = [];

        /// <summary>Whether <see cref="BackupConfig.BackupRoot"/> is a valid absolute path.</summary>
        private bool _backupRootValid;

        /// <summary>Whether <see cref="BackupConfig.SourcesRoot"/> is unset or a valid absolute path.</summary>
        private bool _sourcesRootValid;

        /// <summary>Gets the errors found so far.</summary>
        public List<string> Errors { get; } = [];

        /// <summary>Gets the warnings found so far.</summary>
        public List<string> Warnings { get; } = [];

        /// <summary>
        /// Checks the roots, the accounts, the standalone repositories and the archive folders.
        /// </summary>
        public void Run()
        {
            if (string.IsNullOrWhiteSpace(config.BackupRoot))
            {
                Errors.Add("backupRoot must be set.");
            }
            else
            {
                _backupRootValid = CheckPath(config.BackupRoot, "backupRoot");
            }

            _sourcesRootValid = CheckPath(config.SourcesRoot, "sourcesRoot");
            CheckMasks(config.Exclude, "exclude");

            for (var index = 0; index < config.Accounts.Count; index++)
            {
                CheckAccount(config.Accounts[index], $"accounts[{index}]");
            }

            for (var index = 0; index < config.Repositories.Count; index++)
            {
                CheckStandaloneRepository(config.Repositories[index], $"repositories[{index}]");
            }

            CheckArchiveFolders();
        }

        /// <summary>
        /// Warns when the clone folder is the archive root or an archive folder, or lies inside one.
        /// </summary>
        /// <remarks>
        /// The archive tree is usually synchronised to the cloud; clones inside it would be uploaded too. The check
        /// runs only after <see cref="Run"/>, which resolves the archive folders.
        /// </remarks>
        /// <param name="sourcesRoot">The effective clone folder.</param>
        public void CheckSourcesRoot(string sourcesRoot)
        {
            if (!_sourcesRootValid || !IsAbsolutePath(sourcesRoot))
            {
                return;
            }

            if (_backupRootValid && IsSameOrInside(sourcesRoot, config.BackupRoot))
            {
                Warnings.Add($"sourcesRoot '{sourcesRoot}' is inside backupRoot; the clones would be synchronised together with the archives.");
                return;
            }

            foreach (var (location, folder) in _folders.Where(entry => IsSameOrInside(sourcesRoot, entry.Folder)))
            {
                Warnings.Add($"sourcesRoot '{sourcesRoot}' is inside the archive folder of {location}; the clones would be synchronised together with the archives.");
            }
        }

        /// <summary>
        /// Checks the clone folders that <c>clonePath</c> sets: not a root, outside the clone root, not shared with or
        /// nested in the clone or wiki folder of another entry, and, as a warning, outside the archive tree.
        /// </summary>
        /// <remarks>
        /// Each entry reserves its clone folder and its wiki folder, whether or not the wiki is enabled, because the
        /// wiki may be turned on later. Folders under the clone root cannot collide with each other, because owners
        /// and names are distinct folder names there, and a <c>clonePath</c> that overlaps the clone root is already an
        /// error, so only entries with <c>clonePath</c> are compared. The check runs only after <see cref="Run"/>,
        /// which collects the entries and the archive folders.
        /// </remarks>
        /// <param name="sourcesRoot">The effective clone root.</param>
        public void CheckClonePaths(string sourcesRoot)
        {
            var sourcesRootValid = _sourcesRootValid && IsAbsolutePath(sourcesRoot);
            var reserved = new List<(string Location, string Folder)>();
            foreach (var (location, clonePath) in _clonePaths)
            {
                var field = $"{location}.clonePath";
                if (IsRoot(clonePath))
                {
                    Errors.Add($"{field} must not be the root of a drive or a share.");
                    continue;
                }

                if (sourcesRootValid && Overlaps(clonePath, sourcesRoot))
                {
                    Errors.Add($"{field} '{clonePath}' overlaps sourcesRoot '{sourcesRoot}'; a clone folder set by clonePath must lie outside sourcesRoot.");
                    continue;
                }

                var cloneFolder = Normalize(clonePath);
                string[] folders = [cloneFolder, cloneFolder + RepositoryPathResolver.WikiSuffix];
                var conflict = reserved.FirstOrDefault(entry => folders.Any(folder => Overlaps(folder, entry.Folder)));
                if (conflict.Location is not null)
                {
                    Errors.Add($"{field} '{clonePath}' overlaps the clone folder '{conflict.Folder}' of {conflict.Location}; the clone and wiki folders of two entries must be separate.");
                }

                reserved.AddRange(folders.Select(folder => (location, folder)));
                CheckClonePathOutsideArchives(field, folders);
            }
        }

        /// <summary>
        /// Warns when the clone or wiki folder of an entry is in <see cref="BackupConfig.BackupRoot"/> or in an archive
        /// folder, where it would be synchronised together with the archives.
        /// </summary>
        /// <param name="field">The location of the <c>clonePath</c> in the file.</param>
        /// <param name="folders">The clone folder and the wiki folder of the entry.</param>
        private void CheckClonePathOutsideArchives(string field, string[] folders)
        {
            foreach (var folder in folders)
            {
                if (_backupRootValid && IsSameOrInside(folder, config.BackupRoot))
                {
                    Warnings.Add($"{field} puts the clone folder '{folder}' inside backupRoot; the clone would be synchronised together with the archives.");
                    return;
                }

                var archive = _folders.FirstOrDefault(entry => IsSameOrInside(folder, entry.Folder));
                if (archive.Location is not null)
                {
                    Warnings.Add($"{field} puts the clone folder '{folder}' inside the archive folder of {archive.Location}; the clone would be synchronised together with the archives.");
                    return;
                }
            }
        }

        /// <summary>
        /// Checks one account and its repositories.
        /// </summary>
        /// <param name="account">The account, possibly <see langword="null"/> when the file holds <c>null</c>.</param>
        /// <param name="location">The location of the account in the file.</param>
        private void CheckAccount(AccountConfig? account, string location)
        {
            if (account is null)
            {
                Errors.Add($"{location} must not be null.");
                return;
            }

            if (!GitHubUrl.TryParseAccount(account.Url, out var login))
            {
                Errors.Add($"{location}.url must be an account URL of the form https://github.com/<login>.");
            }
            else if (!_accounts.TryAdd(login, location))
            {
                Errors.Add($"{location}.url names the same account as {_accounts[login]}.url.");
            }

            var accountPathValid = CheckPath(account.Path, $"{location}.path");
            CheckMasks(account.Exclude, $"{location}.exclude");

            for (var index = 0; index < account.Repositories.Count; index++)
            {
                var repositoryLocation = $"{location}.repositories[{index}]";
                var repository = account.Repositories[index];
                if (repository is null)
                {
                    Errors.Add($"{repositoryLocation} must not be null.");
                    continue;
                }

                var nameValid = GitHubUrl.IsValidRepositoryName(repository.Name);
                if (!nameValid)
                {
                    Errors.Add($"{repositoryLocation}.name must be a repository name of letters, digits, '.', '_' and '-'.");
                }

                var pathValid = CheckRepositoryFields(repository.Id, repository.Branch, repository.Path, repositoryLocation);
                CheckClonePath(repository.ClonePath, repositoryLocation);
                if (login is not null && nameValid)
                {
                    CheckDuplicateName($"{login}/{repository.Name}", $"{repositoryLocation}.name");
                }

                if (repository.Path is not null)
                {
                    AddFolder(repositoryLocation, pathValid ? repository.Path : null);
                }
                else if (nameValid && account.Path is not null)
                {
                    AddFolder(repositoryLocation, accountPathValid ? ArchiveFolder.Resolve(config.BackupRoot, account.Path, login ?? string.Empty, repository.Name, null) : null);
                }
                else if (nameValid && login is not null)
                {
                    AddFolder(repositoryLocation, _backupRootValid ? ArchiveFolder.Resolve(config.BackupRoot, null, login, repository.Name, null) : null);
                }
            }
        }

        /// <summary>
        /// Checks one standalone repository.
        /// </summary>
        /// <param name="repository">The repository, possibly <see langword="null"/> when the file holds <c>null</c>.</param>
        /// <param name="location">The location of the repository in the file.</param>
        private void CheckStandaloneRepository(RepositoryConfig? repository, string location)
        {
            if (repository is null)
            {
                Errors.Add($"{location} must not be null.");
                return;
            }

            if (!GitHubUrl.TryParseRepository(repository.Url, out var owner, out var name))
            {
                Errors.Add($"{location}.url must be a repository URL of the form https://github.com/<owner>/<repo>.");
            }

            var pathValid = CheckRepositoryFields(repository.Id, repository.Branch, repository.Path, location);
            CheckClonePath(repository.ClonePath, location);
            if (owner is not null && name is not null)
            {
                CheckDuplicateName($"{owner}/{name}", $"{location}.url");
            }

            if (repository.Path is not null)
            {
                AddFolder(location, pathValid ? repository.Path : null);
            }
            else if (owner is not null && name is not null)
            {
                AddFolder(location, _backupRootValid ? ArchiveFolder.Resolve(config.BackupRoot, null, owner, name, null) : null);
            }
        }

        /// <summary>
        /// Checks the fields shared by both kinds of repository entry and records the ID.
        /// </summary>
        /// <remarks>
        /// An ID of <c>0</c> marks an entry whose ID is resolved by name at the start of a run, so it is valid and
        /// takes no part in the duplicate check.
        /// </remarks>
        /// <param name="id">The GitHub ID, or <c>0</c> when it is unknown.</param>
        /// <param name="branch">The configured branch, or <see langword="null"/>.</param>
        /// <param name="path">The configured archive folder, or <see langword="null"/>.</param>
        /// <param name="location">The location of the entry in the file.</param>
        /// <returns><see langword="true"/> when <paramref name="path"/> is unset or a valid absolute path.</returns>
        private bool CheckRepositoryFields(long id, string? branch, string? path, string location)
        {
            if (id < 0)
            {
                Errors.Add($"{location}.id must be a positive number when set.");
            }
            else if (id > 0 && !_ids.TryAdd(id, location))
            {
                Errors.Add($"{location}.id is the same as {_ids[id]}.id.");
            }

            if (branch is not null && !IsValidBranch(branch))
            {
                Errors.Add($"{location}.branch must be null or a branch name that is not empty and does not start with '-'.");
            }

            return CheckPath(path, $"{location}.path");
        }

        /// <summary>
        /// Reports a <c>clonePath</c> that is set but not absolute and records a valid one for
        /// <see cref="CheckClonePaths"/>.
        /// </summary>
        /// <param name="clonePath">The configured clone folder, or <see langword="null"/>.</param>
        /// <param name="location">The location of the entry in the file.</param>
        private void CheckClonePath(string? clonePath, string location)
        {
            if (clonePath is not null && CheckPath(clonePath, $"{location}.clonePath"))
            {
                _clonePaths.Add((location, clonePath));
            }
        }

        /// <summary>
        /// Reports a repository whose <c>owner/name</c> appeared earlier in the file.
        /// </summary>
        /// <param name="fullName">The <c>owner/name</c> of the repository.</param>
        /// <param name="location">The location of the field that names the repository.</param>
        private void CheckDuplicateName(string fullName, string location)
        {
            if (!_names.TryAdd(fullName, location))
            {
                Errors.Add($"{location} names the same repository as {_names[fullName]}.");
            }
        }

        /// <summary>
        /// Records the archive folder of a repository when it could be resolved.
        /// </summary>
        /// <param name="location">The location of the repository in the file.</param>
        /// <param name="folder">The archive folder, or <see langword="null"/> when an input is invalid.</param>
        private void AddFolder(string location, string? folder)
        {
            if (folder is not null)
            {
                _folders.Add((location, folder));
            }
        }

        /// <summary>
        /// Reports repositories that resolve to the archive folder of an earlier repository.
        /// </summary>
        private void CheckArchiveFolders()
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (location, folder) in _folders)
            {
                var key = Normalize(folder);
                if (!seen.TryAdd(key, location))
                {
                    Errors.Add($"{location} has the same archive folder '{key}' as {seen[key]}.");
                }
            }
        }

        /// <summary>
        /// Reports an optional path that is set but not absolute.
        /// </summary>
        /// <param name="path">The path, or <see langword="null"/> when it is not set.</param>
        /// <param name="location">The location of the path in the file.</param>
        /// <returns><see langword="true"/> when <paramref name="path"/> is unset or a valid absolute path.</returns>
        private bool CheckPath(string? path, string location)
        {
            if (path is null || IsAbsolutePath(path))
            {
                return true;
            }

            Errors.Add($"{location} must be an absolute path.");
            return false;
        }

        /// <summary>
        /// Reports <see langword="null"/> entries of an exclusion list.
        /// </summary>
        /// <param name="masks">The exclusion masks.</param>
        /// <param name="location">The location of the list in the file.</param>
        private void CheckMasks(List<string> masks, string location)
        {
            for (var index = 0; index < masks.Count; index++)
            {
                if (masks[index] is null)
                {
                    Errors.Add($"{location}[{index}] must not be null.");
                }
            }
        }
    }
}
