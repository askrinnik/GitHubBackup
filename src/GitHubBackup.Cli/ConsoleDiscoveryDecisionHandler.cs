using System.Globalization;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Discovery;
using Spectre.Console;

namespace GitHubBackup.Cli;

/// <summary>
/// Asks the user in the console about each new and each renamed repository.
/// </summary>
/// <remarks>
/// Every value that comes from GitHub or from the configuration is shown as plain text, so Spectre.Console never
/// reads it as markup. The questions need an interactive console; the caller checks that before running discovery.
/// </remarks>
/// <param name="console">The outputs of the console application.</param>
internal sealed class ConsoleDiscoveryDecisionHandler(CliConsole console) : IDiscoveryDecisionHandler
{
    /// <summary>The label of the proposed branch and path when the entry sets none.</summary>
    private const string _defaultLabel = "(default)";

    /// <summary>The label of the account of a repository that no tracked account lists.</summary>
    private const string _standaloneLabel = "standalone";

    /// <summary>The actions offered for a new repository.</summary>
    private enum NewChoice
    {
        /// <summary>Adds the repository as proposed.</summary>
        Add,

        /// <summary>Adds the repository with a branch and an archive folder the user types.</summary>
        AddWithChanges,

        /// <summary>Leaves the configuration unchanged.</summary>
        Skip,

        /// <summary>Excludes the repository by its name in the account.</summary>
        ExcludeAccount,

        /// <summary>Excludes the repository by <c>owner/name</c> in the global list.</summary>
        ExcludeGlobal,
    }

    /// <inheritdoc />
    public async Task<NewRepositoryDecision> DecideNewAsync(NewRepositoryProposal proposal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var output = console.Output;
        output.Write(CreateNewTable(proposal));

        var choices = new List<NewChoice> { NewChoice.Add, NewChoice.AddWithChanges, NewChoice.Skip };
        if (proposal.AccountLogin is not null)
        {
            choices.Add(NewChoice.ExcludeAccount);
        }

        choices.Add(NewChoice.ExcludeGlobal);
        var choice = await new SelectionPrompt<NewChoice>()
            .Title("What should happen to this repository?")
            .AddChoices(choices)
            .DefaultValue(NewChoice.Add)
            .UseConverter(Describe)
            .ShowAsync(output, cancellationToken);

        return choice switch
        {
            NewChoice.Add => NewRepositoryDecision.Add(),
            NewChoice.AddWithChanges => await AskChangesAsync(proposal, cancellationToken),
            NewChoice.Skip => NewRepositoryDecision.Skip,
            NewChoice.ExcludeAccount => NewRepositoryDecision.Exclude(ExclusionScope.Account),
            _ => NewRepositoryDecision.Exclude(ExclusionScope.Global),
        };
    }

    /// <inheritdoc />
    public async Task<RenameDecision> DecideRenameAsync(RenameProposal proposal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var output = console.Output;
        var renamed = proposal.Renamed;
        if (IsSameFolder(proposal.CurrentArchiveFolder, proposal.RenamedArchiveFolder))
        {
            output.Write(new Text(
                $"{renamed.Tracked.FullName} was renamed to {renamed.Current.FullName}; its archive folder "
                + $"{proposal.CurrentArchiveFolder} does not depend on the name, so it stays."));
            output.WriteLine();
            return RenameDecision.KeepArchiveFolder;
        }

        output.Write(CreateRenameTable(proposal));
        output.Write(new Text("The clone moves to the new name in either case."));
        output.WriteLine();

        return await new SelectionPrompt<RenameDecision>()
            .Title("What should happen to the archive folder?")
            .AddChoices(RenameDecision.KeepArchiveFolder, RenameDecision.RenameArchiveFolder)
            .DefaultValue(RenameDecision.KeepArchiveFolder)
            .UseConverter(Describe)
            .ShowAsync(output, cancellationToken);
    }

    /// <summary>
    /// Returns the label of an action offered for a new repository.
    /// </summary>
    /// <param name="choice">The action.</param>
    /// <returns>The label, free of markup.</returns>
    private static string Describe(NewChoice choice) => choice switch
    {
        NewChoice.Add => "Add",
        NewChoice.AddWithChanges => "Add with changes (branch and archive folder)",
        NewChoice.Skip => "Skip this time",
        NewChoice.ExcludeAccount => "Exclude from account",
        _ => "Exclude globally",
    };

    /// <summary>
    /// Returns the label of a way to handle the archive folder of a renamed repository.
    /// </summary>
    /// <param name="decision">The way.</param>
    /// <returns>The label, free of markup.</returns>
    private static string Describe(RenameDecision decision) => decision == RenameDecision.KeepArchiveFolder
        ? "b) Keep the current archive folder"
        : "a) Rename the archive folder after the new name";

    /// <summary>
    /// Returns whether two archive folders are the same folder.
    /// </summary>
    /// <param name="first">The first folder.</param>
    /// <param name="second">The second folder.</param>
    /// <returns><see langword="true"/> when they differ at most in case and a trailing separator.</returns>
    private static bool IsSameFolder(string first, string second) =>
        string.Equals(
            first.TrimEnd('\\', '/'),
            second.TrimEnd('\\', '/'),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the table of the repository and the entry proposed for it.
    /// </summary>
    /// <param name="proposal">The new repository.</param>
    /// <returns>The table.</returns>
    private static Table CreateNewTable(NewRepositoryProposal proposal)
    {
        var repository = proposal.Repository;
        var table = CreateTable("New repository");
        AddRow(table, "Repository", repository.FullName);
        AddRow(table, "URL", repository.HtmlUrl);
        AddRow(table, "Owner", repository.Owner);
        AddRow(table, "Visibility", repository.IsPrivate ? "Private" : "Public");
        AddRow(table, "Fork", YesNo(repository.IsFork));
        AddRow(table, "Archived", YesNo(repository.IsArchived));
        AddRow(table, "Default branch", repository.DefaultBranch);
        AddRow(table, "Wiki", YesNo(repository.HasWiki));
        AddRow(table, "Account", proposal.AccountLogin ?? _standaloneLabel);
        AddRow(table, "Proposed name", repository.Name);
        AddRow(table, "Proposed id", repository.Id.ToString(CultureInfo.InvariantCulture));
        AddRow(table, "Proposed branch", _defaultLabel);
        AddRow(table, "Archive folder", proposal.ArchiveFolder);
        return table;
    }

    /// <summary>
    /// Builds the table of the old and the new name of a renamed repository and their archive folders.
    /// </summary>
    /// <param name="proposal">The renamed repository.</param>
    /// <returns>The table.</returns>
    private static Table CreateRenameTable(RenameProposal proposal)
    {
        var table = CreateTable("Renamed repository");
        AddRow(table, "Old name", proposal.Renamed.Tracked.FullName);
        AddRow(table, "New name", proposal.Renamed.Current.FullName);
        AddRow(table, "Current archive folder", proposal.CurrentArchiveFolder);
        AddRow(table, "Archive folder after renaming", proposal.RenamedArchiveFolder);
        return table;
    }

    /// <summary>
    /// Creates an empty two-column table with a plain-text title.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <returns>The table.</returns>
    private static Table CreateTable(string title) =>
        new Table().Title(Markup.Escape(title)).AddColumns("Field", "Value");

    /// <summary>
    /// Adds a row whose cells show their text literally.
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="field">The name of the field.</param>
    /// <param name="value">The value, which may contain characters that look like markup.</param>
    private static void AddRow(Table table, string field, string value) =>
        table.AddRow(new Text(field), new Text(value));

    /// <summary>
    /// Returns <c>Yes</c> or <c>No</c>.
    /// </summary>
    /// <param name="value">The flag.</param>
    /// <returns>The word.</returns>
    private static string YesNo(bool value) => value ? "Yes" : "No";

    /// <summary>
    /// Asks for the branch and the archive folder of the entry.
    /// </summary>
    /// <param name="proposal">The new repository.</param>
    /// <param name="cancellationToken">Cancels the questions.</param>
    /// <returns>The decision that adds the repository with the typed values.</returns>
    private async Task<NewRepositoryDecision> AskChangesAsync(NewRepositoryProposal proposal, CancellationToken cancellationToken)
    {
        var branchPrompt = new TextPrompt<string>(
            "Branch (empty for the default branch " + Markup.Escape(proposal.Repository.DefaultBranch) + "):")
            .AllowEmpty()
            .Validate(value => value.Trim().Length == 0 || BackupConfigValidator.IsValidBranch(value.Trim())
                ? ValidationResult.Success()
                : ValidationResult.Error("The branch must not start with '-'."));
        var branch = (await branchPrompt.ShowAsync(console.Output, cancellationToken)).Trim();

        var pathPrompt = new TextPrompt<string>(
            "Archive folder (empty for " + Markup.Escape(proposal.ArchiveFolder) + "):")
            .AllowEmpty()
            .Validate(value => value.Trim().Length == 0 || BackupConfigValidator.IsAbsolutePath(value.Trim())
                ? ValidationResult.Success()
                : ValidationResult.Error("The archive folder must be an absolute path without invalid characters."));
        var path = (await pathPrompt.ShowAsync(console.Output, cancellationToken)).Trim();

        return NewRepositoryDecision.Add(
            branch.Length == 0 ? null : branch,
            path.Length == 0 || IsSameFolder(path, proposal.ArchiveFolder) ? null : path);
    }
}
