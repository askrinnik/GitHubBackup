using System.IO.Abstractions;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.GitHub;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Discovery;

/// <inheritdoc />
/// <param name="configStore">Saves the changed configuration.</param>
/// <param name="pathResolver">Resolves the archive and clone folders before and after a rename.</param>
/// <param name="remoteUrlProvider">Returns the URLs a moved clone and wiki clone are pointed at.</param>
/// <param name="cloneRelocator">Moves the clones of renamed repositories.</param>
/// <param name="fileSystem">Renames the archive folders.</param>
/// <param name="logger">The logger.</param>
public sealed class DiscoveryDecisionApplier(
    IBackupConfigStore configStore,
    IRepositoryPathResolver pathResolver,
    IRemoteUrlProvider remoteUrlProvider,
    ICloneRelocator cloneRelocator,
    IFileSystem fileSystem,
    ILogger<DiscoveryDecisionApplier> logger) : IDiscoveryDecisionApplier
{
    /// <inheritdoc />
    public async Task<DiscoveryApplyResult> ApplyAsync(
        BackupConfig config,
        DiscoveryResult discovery,
        IDiscoveryDecisionHandler handler,
        string gitPath,
        CancellationToken cancellationToken)
    {
        var newItems = await DecideNewAsync(config, discovery, handler, cancellationToken);
        var renames = await DecideRenamesAsync(config, discovery, handler, cancellationToken);
        var statusEntries = discovery.Missing.Select(item => (item, Status: RepositoryStatus.Unavailable))
            .Concat(discovery.Returned.Select(item => (item.Tracked, Status: RepositoryStatus.Active)))
            .Select(pair => (Entry: FindEntry(config, pair.Item1), pair.Status, Name: pair.Item1.FullName))
            .ToList();

        var movedArchives = new List<(string From, string To)>();
        foreach (var rename in renames)
        {
            rename.Applied = MoveArchiveFolder(rename, movedArchives);
        }

        var added = new List<string>();
        var skipped = new List<string>();
        var excluded = new List<string>();
        bool changed;
        try
        {
            changed = ApplyNew(config, newItems, added, skipped, excluded);
            foreach (var (entry, status, name) in statusEntries)
            {
                entry.Status = status;
                if (status == RepositoryStatus.Unavailable)
                {
                    DiscoveryDecisionApplierLog.MarkedUnavailable(logger, name);
                }
                else
                {
                    DiscoveryDecisionApplierLog.Reactivated(logger, name);
                }
            }

            foreach (var rename in renames)
            {
                ApplyRename(config, rename);
            }

            changed |= statusEntries.Count > 0 || renames.Count > 0;
            if (changed)
            {
                await configStore.SaveAsync(config, cancellationToken);
            }
        }
        catch
        {
            RollBack(movedArchives);
            throw;
        }

        var outcomes = new List<RenameOutcome>();
        foreach (var rename in renames)
        {
            var clone = await cloneRelocator.RelocateAsync(
                gitPath, rename.OldClone, rename.NewClone, rename.NewRemoteUrl, cancellationToken);
            var wiki = await cloneRelocator.RelocateAsync(
                gitPath, rename.OldWikiClone, rename.NewWikiClone, rename.NewWikiUrl, cancellationToken);
            outcomes.Add(new(
                rename.Renamed.Tracked.FullName,
                rename.Renamed.Current.FullName,
                rename.Requested,
                rename.Applied,
                clone,
                wiki));
        }

        return new(
            added,
            skipped,
            excluded,
            [.. discovery.Missing.Select(item => item.FullName)],
            [.. discovery.Returned.Select(item => item.Tracked.FullName)],
            outcomes,
            changed);
    }

    /// <summary>
    /// Returns the URL of a standalone repository as <c>backup-config.json</c> stores it.
    /// </summary>
    /// <param name="owner">The login of the owner.</param>
    /// <param name="name">The repository name.</param>
    /// <returns>The URL, <c>https://github.com/&lt;owner&gt;/&lt;name&gt;</c>.</returns>
    private static string RepositoryUrl(string owner, string name) => $"https://github.com/{owner}/{name}";

    /// <summary>
    /// Returns the tracked account with the given login.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="login">The login, compared without regard to case.</param>
    /// <returns>The account, or <see langword="null"/> when no account has that login.</returns>
    private static AccountConfig? FindAccount(BackupConfig config, string login) =>
        config.Accounts.Find(account =>
            GitHubUrl.TryParseAccount(account.Url, out var accountLogin)
            && string.Equals(accountLogin, login, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the configuration entry of a tracked repository.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="tracked">The tracked repository.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="ArgumentException">The configuration does not list the repository.</exception>
    private static ConfigEntry FindEntry(BackupConfig config, TrackedRepository tracked)
    {
        if (tracked.AccountLogin is not null)
        {
            var account = FindAccount(config, tracked.AccountLogin);
            var repository = account?.Repositories.Find(item => item.Id == tracked.Id);
            if (account is not null && repository is not null)
            {
                return new(account, repository, null);
            }
        }
        else if (config.Repositories.Find(item => item.Id == tracked.Id) is { } standalone)
        {
            return new(null, null, standalone);
        }

        throw new ArgumentException(
            $"The repository '{tracked.FullName}' with id {tracked.Id} is not in the configuration.", nameof(config));
    }

    /// <summary>
    /// Builds a proposal for every new repository and asks the handler about it, without changing anything.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="discovery">The classified repositories.</param>
    /// <param name="handler">The handler that decides.</param>
    /// <param name="cancellationToken">Cancels the decisions.</param>
    /// <returns>The new repositories with their decisions, accounts first.</returns>
    /// <exception cref="ArgumentException">
    /// An account is not in the configuration, or a decision is not valid for its repository.
    /// </exception>
    private async Task<List<NewItem>> DecideNewAsync(
        BackupConfig config, DiscoveryResult discovery, IDiscoveryDecisionHandler handler, CancellationToken cancellationToken)
    {
        var items = new List<NewItem>();
        foreach (var item in discovery.NewInAccounts)
        {
            var account = FindAccount(config, item.AccountLogin)
                ?? throw new ArgumentException($"The account '{item.AccountLogin}' is not in the configuration.", nameof(discovery));
            var folder = pathResolver.GetArchiveFolder(
                config, account, new AccountRepositoryConfig { Name = item.Repository.Name, Id = item.Repository.Id });
            var decision = await handler.DecideNewAsync(new(item.Repository, item.AccountLogin, folder), cancellationToken);
            EnsureValid(decision, item.Repository, standalone: false);
            items.Add(new(item.Repository, account, decision));
        }

        foreach (var repository in discovery.NewStandalone)
        {
            var folder = pathResolver.GetArchiveFolder(
                config, new RepositoryConfig { Url = RepositoryUrl(repository.Owner, repository.Name), Id = repository.Id });
            var decision = await handler.DecideNewAsync(new(repository, null, folder), cancellationToken);
            EnsureValid(decision, repository, standalone: true);
            items.Add(new(repository, null, decision));
        }

        return items;
    }

    /// <summary>
    /// Rejects a decision that cannot be applied to its repository.
    /// </summary>
    /// <param name="decision">The decision.</param>
    /// <param name="repository">The repository it is about.</param>
    /// <param name="standalone">Whether the repository is outside every tracked account.</param>
    /// <exception cref="ArgumentException">
    /// An exclusion has no scope, or excludes a standalone repository in an account's masks, which it has none of.
    /// </exception>
    private static void EnsureValid(NewRepositoryDecision decision, GitHubRepository repository, bool standalone)
    {
        if (decision.Action != NewRepositoryAction.Exclude)
        {
            return;
        }

        if (decision.ExclusionScope is null)
        {
            throw new ArgumentException(
                $"The exclusion of '{repository.FullName}' does not name a list of masks.", nameof(decision));
        }

        if (standalone && decision.ExclusionScope == ExclusionScope.Account)
        {
            throw new ArgumentException(
                $"'{repository.FullName}' belongs to no tracked account; it can be excluded only globally.",
                nameof(decision));
        }
    }

    /// <summary>
    /// Plans every rename and asks the handler about its archive folder, without changing anything.
    /// </summary>
    /// <param name="config">The configuration.</param>
    /// <param name="discovery">The classified repositories.</param>
    /// <param name="handler">The handler that decides.</param>
    /// <param name="cancellationToken">Cancels the decisions.</param>
    /// <returns>The planned renames in the order of <see cref="DiscoveryResult.Renamed"/>.</returns>
    /// <exception cref="ArgumentException">An entry is not in the configuration or a new name is not valid.</exception>
    private async Task<List<RenamePlan>> DecideRenamesAsync(
        BackupConfig config, DiscoveryResult discovery, IDiscoveryDecisionHandler handler, CancellationToken cancellationToken)
    {
        var plans = new List<RenamePlan>();
        foreach (var renamed in discovery.Renamed)
        {
            var entry = FindEntry(config, renamed.Tracked);
            var current = renamed.Current;

            // A standalone entry stays standalone; an account entry follows its new owner when that owner is tracked
            // and becomes standalone otherwise.
            var targetAccount = entry.Account is null ? null : FindAccount(config, current.Owner);
            var currentFolder = entry.Account is not null
                ? pathResolver.GetArchiveFolder(config, entry.Account, entry.AccountRepository!)
                : pathResolver.GetArchiveFolder(config, entry.Standalone!);
            var renamedFolder = targetAccount is not null
                ? pathResolver.GetArchiveFolder(
                    config, targetAccount, new AccountRepositoryConfig { Name = current.Name, Id = current.Id, Path = entry.Path })
                : pathResolver.GetArchiveFolder(
                    config, new RepositoryConfig { Url = RepositoryUrl(current.Owner, current.Name), Id = current.Id, Path = entry.Path });

            var requested = await handler.DecideRenameAsync(new(renamed, currentFolder, renamedFolder), cancellationToken);
            plans.Add(new()
            {
                Renamed = renamed,
                Requested = requested,
                Entry = entry,
                TargetAccount = targetAccount,
                CurrentArchiveFolder = currentFolder,
                RenamedArchiveFolder = renamedFolder,
                OldClone = pathResolver.GetCloneFolder(config, renamed.Tracked.Owner, renamed.Tracked.Name),
                NewClone = pathResolver.GetCloneFolder(config, current.Owner, current.Name),
                OldWikiClone = pathResolver.GetWikiCloneFolder(config, renamed.Tracked.Owner, renamed.Tracked.Name),
                NewWikiClone = pathResolver.GetWikiCloneFolder(config, current.Owner, current.Name),
                NewRemoteUrl = remoteUrlProvider.GetRepositoryUrl(current.Owner, current.Name),
                NewWikiUrl = remoteUrlProvider.GetWikiUrl(current.Owner, current.Name),
            });
        }

        return plans;
    }

    /// <summary>
    /// Renames the archive folder of a renamed repository when the decision asks for it.
    /// </summary>
    /// <param name="rename">The planned rename.</param>
    /// <param name="movedArchives">Receives the folder when it was renamed, for a rollback.</param>
    /// <returns>
    /// The decision carried out: <see cref="RenameDecision.KeepArchiveFolder"/> when the folder was to be renamed but
    /// the target exists or the rename failed.
    /// </returns>
    private RenameDecision MoveArchiveFolder(RenamePlan rename, List<(string From, string To)> movedArchives)
    {
        var from = rename.CurrentArchiveFolder;
        var to = rename.RenamedArchiveFolder;
        if (rename.Requested == RenameDecision.KeepArchiveFolder
            || string.Equals(from, to, StringComparison.OrdinalIgnoreCase)
            || !fileSystem.Directory.Exists(from))
        {
            return rename.Requested;
        }

        if (fileSystem.Directory.Exists(to) || fileSystem.File.Exists(to))
        {
            DiscoveryDecisionApplierLog.ArchiveFolderTargetExists(logger, from, to);
            return RenameDecision.KeepArchiveFolder;
        }

        try
        {
            var parent = fileSystem.Path.GetDirectoryName(to);
            if (parent is not null)
            {
                fileSystem.Directory.CreateDirectory(parent);
            }

            fileSystem.Directory.Move(from, to);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            DiscoveryDecisionApplierLog.ArchiveFolderNotRenamed(logger, from, to, exception);
            return RenameDecision.KeepArchiveFolder;
        }

        movedArchives.Add((from, to));
        DiscoveryDecisionApplierLog.ArchiveFolderRenamed(logger, from, to);
        return RenameDecision.RenameArchiveFolder;
    }

    /// <summary>
    /// Renames the archive folders back in reverse order, logging each one that cannot be renamed back.
    /// </summary>
    /// <param name="movedArchives">The renamed folders.</param>
    private void RollBack(List<(string From, string To)> movedArchives)
    {
        for (var index = movedArchives.Count - 1; index >= 0; index--)
        {
            var (from, to) = movedArchives[index];
            try
            {
                fileSystem.Directory.Move(to, from);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                DiscoveryDecisionApplierLog.ArchiveFolderNotRestored(logger, to, from, exception);
            }
        }
    }

    /// <summary>
    /// Adds the entries and masks the decisions about new repositories ask for.
    /// </summary>
    /// <param name="config">The configuration to change.</param>
    /// <param name="items">The new repositories with their decisions.</param>
    /// <param name="added">Receives the <c>owner/name</c> of each added repository.</param>
    /// <param name="skipped">Receives the <c>owner/name</c> of each skipped repository.</param>
    /// <param name="excluded">Receives the <c>owner/name</c> of each excluded repository.</param>
    /// <returns><see langword="true"/> when <paramref name="config"/> changed.</returns>
    private bool ApplyNew(
        BackupConfig config, List<NewItem> items, List<string> added, List<string> skipped, List<string> excluded)
    {
        var changed = false;
        foreach (var (repository, account, decision) in items)
        {
            switch (decision.Action)
            {
                case NewRepositoryAction.Add when account is not null:
                    account.Repositories.Add(new()
                    {
                        Name = repository.Name,
                        Id = repository.Id,
                        Branch = decision.Branch,
                        Path = decision.Path,
                        Status = RepositoryStatus.Active,
                    });
                    break;
                case NewRepositoryAction.Add:
                    config.Repositories.Add(new()
                    {
                        Url = RepositoryUrl(repository.Owner, repository.Name),
                        Id = repository.Id,
                        Branch = decision.Branch,
                        Path = decision.Path,
                        Status = RepositoryStatus.Active,
                    });
                    break;
                case NewRepositoryAction.Exclude:
                    var (masks, mask) = decision.ExclusionScope == ExclusionScope.Account
                        ? (account!.Exclude, repository.Name)
                        : (config.Exclude, repository.FullName);
                    if (!masks.Contains(mask, StringComparer.OrdinalIgnoreCase))
                    {
                        masks.Add(mask);
                        changed = true;
                    }

                    excluded.Add(repository.FullName);
                    DiscoveryDecisionApplierLog.Excluded(logger, repository.FullName, mask);
                    continue;
                default:
                    skipped.Add(repository.FullName);
                    continue;
            }

            changed = true;
            added.Add(repository.FullName);
            DiscoveryDecisionApplierLog.Added(logger, repository.FullName);
        }

        return changed;
    }

    /// <summary>
    /// Writes the new name of a renamed repository to the configuration, moving the entry to the list of its new
    /// owner when the owner changed, and keeps the old archive folder in <c>path</c> when the decision carried out
    /// asks for it.
    /// </summary>
    /// <param name="config">The configuration to change.</param>
    /// <param name="rename">The planned rename.</param>
    private void ApplyRename(BackupConfig config, RenamePlan rename)
    {
        var entry = rename.Entry;
        var current = rename.Renamed.Current;
        var path = rename.Applied == RenameDecision.KeepArchiveFolder
            && !string.Equals(rename.CurrentArchiveFolder, rename.RenamedArchiveFolder, StringComparison.OrdinalIgnoreCase)
            ? rename.CurrentArchiveFolder
            : entry.Path;

        if (entry.Standalone is not null)
        {
            entry.Standalone.Url = RepositoryUrl(current.Owner, current.Name);
            entry.Standalone.Path = path;
        }
        else if (rename.TargetAccount == entry.Account)
        {
            entry.AccountRepository!.Name = current.Name;
            entry.AccountRepository.Path = path;
        }
        else
        {
            var repository = entry.AccountRepository!;
            entry.Account!.Repositories.Remove(repository);
            if (rename.TargetAccount is not null)
            {
                rename.TargetAccount.Repositories.Add(new()
                {
                    Name = current.Name,
                    Id = repository.Id,
                    Branch = repository.Branch,
                    Path = path,
                    Status = repository.Status,
                });
            }
            else
            {
                config.Repositories.Add(new()
                {
                    Url = RepositoryUrl(current.Owner, current.Name),
                    Id = repository.Id,
                    Branch = repository.Branch,
                    Path = path,
                    Status = repository.Status,
                });
            }
        }

        DiscoveryDecisionApplierLog.Renamed(logger, rename.Renamed.Tracked.FullName, current.FullName);
    }

    /// <summary>
    /// A new repository with the decision about it.
    /// </summary>
    /// <param name="Repository">The repository.</param>
    /// <param name="Account">The tracked account it belongs to, or <see langword="null"/> for a standalone repository.</param>
    /// <param name="Decision">The decision of the handler.</param>
    private sealed record NewItem(GitHubRepository Repository, AccountConfig? Account, NewRepositoryDecision Decision);

    /// <summary>
    /// A configuration entry: a repository of an account or a standalone repository.
    /// </summary>
    /// <param name="Account">The account that lists the repository, or <see langword="null"/> for a standalone entry.</param>
    /// <param name="AccountRepository">The entry in <paramref name="Account"/>, or <see langword="null"/> for a standalone entry.</param>
    /// <param name="Standalone">The standalone entry, or <see langword="null"/> for an account entry.</param>
    private sealed record ConfigEntry(
        AccountConfig? Account, AccountRepositoryConfig? AccountRepository, RepositoryConfig? Standalone)
    {
        /// <summary>Gets the <c>path</c> of the entry.</summary>
        public string? Path => AccountRepository?.Path ?? Standalone?.Path;

        /// <summary>Gets or sets the status of the entry.</summary>
        public RepositoryStatus Status
        {
            get => AccountRepository?.Status ?? Standalone!.Status;
            set
            {
                if (AccountRepository is not null)
                {
                    AccountRepository.Status = value;
                }
                else
                {
                    Standalone!.Status = value;
                }
            }
        }
    }

    /// <summary>
    /// Everything a rename needs, resolved before anything changes.
    /// </summary>
    private sealed class RenamePlan
    {
        /// <summary>Gets the entry and the repository under its current name.</summary>
        public required RenamedRepository Renamed { get; init; }

        /// <summary>Gets the configuration entry.</summary>
        public required ConfigEntry Entry { get; init; }

        /// <summary>Gets the account the entry belongs to after the rename, or <see langword="null"/> for a standalone entry.</summary>
        public required AccountConfig? TargetAccount { get; init; }

        /// <summary>Gets the archive folder before the rename.</summary>
        public required string CurrentArchiveFolder { get; init; }

        /// <summary>Gets the archive folder after <see cref="RenameDecision.RenameArchiveFolder"/>.</summary>
        public required string RenamedArchiveFolder { get; init; }

        /// <summary>Gets the clone folder under the old name.</summary>
        public required string OldClone { get; init; }

        /// <summary>Gets the clone folder under the new name.</summary>
        public required string NewClone { get; init; }

        /// <summary>Gets the wiki clone folder under the old name.</summary>
        public required string OldWikiClone { get; init; }

        /// <summary>Gets the wiki clone folder under the new name.</summary>
        public required string NewWikiClone { get; init; }

        /// <summary>Gets the URL of the repository under the new name.</summary>
        public required string NewRemoteUrl { get; init; }

        /// <summary>Gets the URL of the wiki under the new name.</summary>
        public required string NewWikiUrl { get; init; }

        /// <summary>Gets the decision of the handler.</summary>
        public required RenameDecision Requested { get; init; }

        /// <summary>Gets or sets the decision carried out.</summary>
        public RenameDecision Applied { get; set; }
    }
}
