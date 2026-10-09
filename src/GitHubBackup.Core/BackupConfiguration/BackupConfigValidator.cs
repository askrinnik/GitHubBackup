using System.Buffers;
using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Validates a <see cref="BackupConfig"/>: absolute paths, GitHub URLs, repository names and identifiers, unique
/// accounts and repositories, distinct archive folders, and clones kept out of the archive tree.
/// </summary>
/// <remarks>
/// An entry whose URL, name or path is already reported as invalid is left out of the duplicate and archive folder
/// checks, so one mistake produces one error.
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
    private static bool IsAbsolutePath(string path) =>
        !string.IsNullOrWhiteSpace(path)
        && !path.AsSpan().ContainsAny(_invalidPathCharacters)
        && path.LastIndexOf(':') is -1 or 1
        && !IsDeviceNamespacePath(path)
        && Path.IsPathFullyQualified(path);

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
        /// <param name="id">The GitHub ID.</param>
        /// <param name="branch">The configured branch, or <see langword="null"/>.</param>
        /// <param name="path">The configured archive folder, or <see langword="null"/>.</param>
        /// <param name="location">The location of the entry in the file.</param>
        /// <returns><see langword="true"/> when <paramref name="path"/> is unset or a valid absolute path.</returns>
        private bool CheckRepositoryFields(long id, string? branch, string? path, string location)
        {
            if (id <= 0)
            {
                Errors.Add($"{location}.id must be a positive number.");
            }
            else if (!_ids.TryAdd(id, location))
            {
                Errors.Add($"{location}.id is the same as {_ids[id]}.id.");
            }

            // A branch is passed to git, where a leading '-' would be read as an option.
            if (branch is not null && (string.IsNullOrWhiteSpace(branch) || branch.StartsWith('-')))
            {
                Errors.Add($"{location}.branch must be null or a branch name that is not empty and does not start with '-'.");
            }

            return CheckPath(path, $"{location}.path");
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
