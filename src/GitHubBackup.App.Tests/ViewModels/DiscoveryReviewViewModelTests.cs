using GitHubBackup.App.ViewModels;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.GitHub;

namespace GitHubBackup.App.Tests.ViewModels;

/// <summary>
/// Tests <see cref="DiscoveryReviewViewModel"/> and its rows.
/// </summary>
public sealed class DiscoveryReviewViewModelTests
{
    private const string _toolFolder = @"C:\backup\octo\tool";

    private static readonly GitHubRepository _tool =
        new(10, "octo", "tool", true, true, false, true, "main", "https://github.com/octo/tool");

    private static readonly GitHubRepository _lib =
        new(20, "acme", "lib", false, false, true, false, "trunk", "https://github.com/acme/lib");

    private static readonly NewRepositoryProposal _accountProposal = new(_tool, "octo", _toolFolder);

    private static readonly NewRepositoryProposal _standaloneProposal = new(_lib, null, @"C:\backup\acme\lib");

    private static readonly RenameProposal _renameProposal = new(
        new(new("octo", "app", 1, RepositoryStatus.Active, "octo"), Repo(1, "octo", "app2")),
        @"C:\backup\octo\app",
        @"C:\backup\octo\app2");

    private static readonly RenameProposal _renameWithOwnPath = new(
        new(new("octo", "web", 2, RepositoryStatus.Active, "octo"), Repo(2, "octo", "site")),
        @"D:\archives\web",
        @"D:\archives\web\");

    private static readonly TrackedRepository _missing = new("octo", "gone", 3, RepositoryStatus.Active, "octo");

    private static readonly ReturnedRepository _returned =
        new(new("acme", "old", 4, RepositoryStatus.Unavailable, null), Repo(4, "acme", "old"));

    [Fact]
    public void Rows_AllFindings_ListsNewRenamedMissingAndReturnedInThatOrder()
    {
        var viewModel = Create(new([_accountProposal, _standaloneProposal], [_renameProposal], [_missing], [_returned]));

        viewModel.Rows.Select(row => (row.Kind, row.Repository)).ShouldBe(
        [
            ("New", "octo/tool"),
            ("New", "acme/lib"),
            ("Renamed", "octo/app2"),
            ("Missing", "octo/gone"),
            ("Returned", "acme/old"),
        ]);
        viewModel.Rows.Select(row => row.IsActionable).ShouldBe([true, true, true, false, false]);
    }

    [Fact]
    public void NewRow_AccountRepository_ShowsGitHubAttributesAndProposedEntry()
    {
        var row = Create(new([_accountProposal], [], [], [])).NewRows.ShouldHaveSingleItem();

        row.Url.ShouldBe("https://github.com/octo/tool");
        row.Owner.ShouldBe("octo");
        row.Visibility.ShouldBe("Private");
        row.Fork.ShouldBe("Yes");
        row.Archived.ShouldBe("No");
        row.DefaultBranch.ShouldBe("main");
        row.Wiki.ShouldBe("Yes");
        row.Account.ShouldBe("octo");
        row.Repository.ShouldBe("octo/tool");
        row.Id.ShouldBe("10");
        row.Branch.ShouldBeEmpty();
        row.Path.ShouldBe(_toolFolder);
    }

    [Fact]
    public void NewRow_Standalone_ShowsStandaloneAndPublicAttributes()
    {
        var row = Create(new([_standaloneProposal], [], [], [])).NewRows.ShouldHaveSingleItem();

        row.Account.ShouldBe(DiscoveryRowViewModel.StandaloneLabel);
        row.Visibility.ShouldBe("Public");
        row.Fork.ShouldBe("No");
        row.Archived.ShouldBe("Yes");
        row.DefaultBranch.ShouldBe("trunk");
        row.Wiki.ShouldBe("No");
    }

    [Fact]
    public void NewRow_Default_AddsWithDefaultBranchAndDerivedFolder()
    {
        var row = Create(new([_accountProposal], [], [], [])).NewRows.ShouldHaveSingleItem();

        row.Choice.ShouldBe(NewRepositoryChoice.Add);
        row.ToDecision().ShouldBe(NewRepositoryDecision.Add());
    }

    [Fact]
    public void NewRow_AccountRepository_OffersBothExclusionScopes()
    {
        var row = Create(new([_accountProposal], [], [], [])).NewRows.ShouldHaveSingleItem();

        row.Choices.Select(option => option.Value).ShouldBe(
        [
            NewRepositoryChoice.Add,
            NewRepositoryChoice.Skip,
            NewRepositoryChoice.ExcludeFromAccount,
            NewRepositoryChoice.ExcludeGlobally,
        ]);
    }

    [Fact]
    public void NewRow_Standalone_OffersOnlyGlobalExclusion()
    {
        var row = Create(new([_standaloneProposal], [], [], [])).NewRows.ShouldHaveSingleItem();

        row.Choices.Select(option => option.Value).ShouldBe(
            [NewRepositoryChoice.Add, NewRepositoryChoice.Skip, NewRepositoryChoice.ExcludeGlobally]);
    }

    [Theory]
    [InlineData(NewRepositoryChoice.Skip)]
    [InlineData(NewRepositoryChoice.ExcludeFromAccount)]
    [InlineData(NewRepositoryChoice.ExcludeGlobally)]
    public void NewRow_Choice_MapsToDecision(NewRepositoryChoice choice)
    {
        var row = Create(new([_accountProposal], [], [], [])).NewRows.ShouldHaveSingleItem();

        row.Choice = choice;

        row.ToDecision().ShouldBe(choice switch
        {
            NewRepositoryChoice.Skip => NewRepositoryDecision.Skip,
            NewRepositoryChoice.ExcludeFromAccount => NewRepositoryDecision.Exclude(ExclusionScope.Account),
            _ => NewRepositoryDecision.Exclude(ExclusionScope.Global),
        });
    }

    [Theory]
    [InlineData("", @"C:\backup\octo\tool", null, null)]
    [InlineData("  develop  ", @"  D:\archives\tool  ", "develop", @"D:\archives\tool")]
    [InlineData("main", "", "main", null)]
    [InlineData("", @"c:\BACKUP\octo\tool\", null, null)]
    public void NewRow_EditedBranchAndPath_MapsToTrimmedValuesOrNull(
        string branch, string path, string? expectedBranch, string? expectedPath)
    {
        var row = Create(new([_accountProposal], [], [], [])).NewRows.ShouldHaveSingleItem();

        row.Branch = branch;
        row.Path = path;

        row.HasErrors.ShouldBeFalse();
        row.ToDecision().ShouldBe(NewRepositoryDecision.Add(expectedBranch, expectedPath));
    }

    [Theory]
    [InlineData("-x")]
    [InlineData(" --upload-pack=evil")]
    public void NewRow_InvalidBranch_ShowsErrorAndDisablesApply(string branch)
    {
        var viewModel = Create(new([_accountProposal], [], [], []));
        var row = viewModel.NewRows.ShouldHaveSingleItem();

        row.Branch = branch;

        row.BranchError.ShouldBe(NewDiscoveryRowViewModel.InvalidBranchMessage);
        row.HasBranchError.ShouldBeTrue();
        row.HasErrors.ShouldBeTrue();
        viewModel.ApplyCommand.CanExecute(null).ShouldBeFalse();
    }

    [Theory]
    [InlineData("relative\\folder")]
    [InlineData(@"C:\bad|name")]
    [InlineData(@"\\?\C:\backup")]
    public void NewRow_InvalidPath_ShowsErrorAndDisablesApply(string path)
    {
        var viewModel = Create(new([_accountProposal], [], [], []));
        var row = viewModel.NewRows.ShouldHaveSingleItem();

        row.Path = path;

        row.PathError.ShouldBe(NewDiscoveryRowViewModel.InvalidPathMessage);
        row.HasPathError.ShouldBeTrue();
        viewModel.ApplyCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void NewRow_InvalidValueCorrected_EnablesApply()
    {
        var viewModel = Create(new([_accountProposal], [], [], []));
        var row = viewModel.NewRows.ShouldHaveSingleItem();
        row.Branch = "-x";
        var changes = 0;
        viewModel.ApplyCommand.CanExecuteChanged += (_, _) => changes++;

        row.Branch = "develop";

        changes.ShouldBeGreaterThan(0);
        viewModel.ApplyCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public void NewRow_InvalidValueOfSkippedRow_DoesNotBlockApply()
    {
        var viewModel = Create(new([_accountProposal], [], [], []));
        var row = viewModel.NewRows.ShouldHaveSingleItem();
        row.Path = "relative";

        row.Choice = NewRepositoryChoice.Skip;

        row.HasErrors.ShouldBeFalse();
        viewModel.ApplyCommand.CanExecute(null).ShouldBeTrue();
        row.ToDecision().ShouldBe(NewRepositoryDecision.Skip);
    }

    [Fact]
    public void RenamedRow_DifferentFolders_ShowsNamesAndFoldersAndKeepsByDefault()
    {
        var row = Create(new([], [_renameProposal], [], [])).RenamedRows.ShouldHaveSingleItem();

        row.OldName.ShouldBe("octo/app");
        row.Repository.ShouldBe("octo/app2");
        row.CurrentArchiveFolder.ShouldBe(@"C:\backup\octo\app");
        row.RenamedArchiveFolder.ShouldBe(@"C:\backup\octo\app2");
        row.CanChooseVariant.ShouldBeTrue();
        row.Variant.ShouldBe(RenameDecision.KeepArchiveFolder);
        row.Variants.Select(option => option.Value).ShouldBe(
            [RenameDecision.RenameArchiveFolder, RenameDecision.KeepArchiveFolder]);
    }

    [Fact]
    public void RenamedRow_VariantA_MapsToRenameArchiveFolder()
    {
        var row = Create(new([], [_renameProposal], [], [])).RenamedRows.ShouldHaveSingleItem();

        row.Variant = RenameDecision.RenameArchiveFolder;

        row.ToDecision().ShouldBe(RenameDecision.RenameArchiveFolder);
    }

    [Fact]
    public void RenamedRow_SameFolder_CannotChooseAndKeepsArchiveFolder()
    {
        var row = Create(new([], [_renameWithOwnPath], [], [])).RenamedRows.ShouldHaveSingleItem();

        row.Variant = RenameDecision.RenameArchiveFolder;

        row.CanChooseVariant.ShouldBeFalse();
        row.Variant.ShouldBe(RenameDecision.KeepArchiveFolder);
        row.ToDecision().ShouldBe(RenameDecision.KeepArchiveFolder);
    }

    [Fact]
    public void MissingRow_ShowsEntryWithoutGitHubAttributes()
    {
        var row = Create(new([], [], [_missing], [])).Rows.ShouldHaveSingleItem();

        row.ShouldBeOfType<MissingDiscoveryRowViewModel>();
        row.Repository.ShouldBe("octo/gone");
        row.Account.ShouldBe("octo");
        row.Id.ShouldBe("3");
        row.Url.ShouldBeEmpty();
        row.Note.ShouldNotBeEmpty();
        row.IsActionable.ShouldBeFalse();
    }

    [Fact]
    public void ReturnedRow_ShowsGitHubAttributes()
    {
        var row = Create(new([], [], [], [_returned])).Rows.ShouldHaveSingleItem();

        row.ShouldBeOfType<ReturnedDiscoveryRowViewModel>();
        row.Repository.ShouldBe("acme/old");
        row.Account.ShouldBe(DiscoveryRowViewModel.StandaloneLabel);
        row.Url.ShouldBe("https://github.com/acme/old");
        row.IsActionable.ShouldBeFalse();
    }

    [Fact]
    public void SkipAll_SetsSkipOnEveryNewRow()
    {
        var viewModel = Create(new([_accountProposal, _standaloneProposal], [], [], []));

        viewModel.SkipAllCommand.Execute(null);

        viewModel.NewRows.Select(row => row.Choice).ShouldBe([NewRepositoryChoice.Skip, NewRepositoryChoice.Skip]);
    }

    [Fact]
    public void AddAll_AfterSkipAll_SetsAddOnEveryNewRow()
    {
        var viewModel = Create(new([_accountProposal, _standaloneProposal], [], [], []));
        viewModel.SkipAllCommand.Execute(null);

        viewModel.AddAllCommand.Execute(null);

        viewModel.NewRows.Select(row => row.Choice).ShouldBe([NewRepositoryChoice.Add, NewRepositoryChoice.Add]);
    }

    [Fact]
    public void ExcludeAll_UsesAccountScopeForAccountAndGlobalForStandalone()
    {
        var viewModel = Create(new([_accountProposal, _standaloneProposal], [], [], []));

        viewModel.ExcludeAllCommand.Execute(null);

        viewModel.GetNewDecisions().ShouldBe(new Dictionary<long, NewRepositoryDecision>
        {
            [10] = NewRepositoryDecision.Exclude(ExclusionScope.Account),
            [20] = NewRepositoryDecision.Exclude(ExclusionScope.Global),
        });
    }

    [Fact]
    public void RenameAllArchiveFolders_SetsVariantAOnlyWhereChoosable()
    {
        var viewModel = Create(new([], [_renameProposal, _renameWithOwnPath], [], []));

        viewModel.RenameAllArchiveFoldersCommand.Execute(null);

        viewModel.GetRenameDecisions().ShouldBe(new Dictionary<long, RenameDecision>
        {
            [1] = RenameDecision.RenameArchiveFolder,
            [2] = RenameDecision.KeepArchiveFolder,
        });
    }

    [Fact]
    public void KeepAllArchiveFolders_AfterRenameAll_SetsVariantB()
    {
        var viewModel = Create(new([], [_renameProposal], [], []));
        viewModel.RenameAllArchiveFoldersCommand.Execute(null);

        viewModel.KeepAllArchiveFoldersCommand.Execute(null);

        viewModel.RenamedRows.ShouldHaveSingleItem().Variant.ShouldBe(RenameDecision.KeepArchiveFolder);
    }

    [Fact]
    public void ActionsForAll_InformationalRows_StayUnchanged()
    {
        var viewModel = Create(new([_accountProposal], [], [_missing], [_returned]));

        viewModel.ExcludeAllCommand.Execute(null);
        viewModel.RenameAllArchiveFoldersCommand.Execute(null);

        viewModel.Rows.Count.ShouldBe(3);
        viewModel.Rows.Skip(1).ShouldAllBe(row => !row.IsActionable && !row.HasErrors);
    }

    [Fact]
    public void ApplyText_ActionableRows_IsApply()
    {
        var viewModel = Create(new([_accountProposal], [], [_missing], []));

        viewModel.HasActionableRows.ShouldBeTrue();
        viewModel.ApplyText.ShouldBe(DiscoveryReviewViewModel.ApplyLabel);
    }

    [Fact]
    public void ApplyText_OnlyMissingAndReturned_IsContinueAndApplyIsEnabled()
    {
        var viewModel = Create(new([], [], [_missing], [_returned]));

        viewModel.HasActionableRows.ShouldBeFalse();
        viewModel.HasNewRows.ShouldBeFalse();
        viewModel.HasRenamedRows.ShouldBeFalse();
        viewModel.ApplyText.ShouldBe(DiscoveryReviewViewModel.ContinueLabel);
        viewModel.ApplyCommand.CanExecute(null).ShouldBeTrue();
        viewModel.GetNewDecisions().ShouldBeEmpty();
        viewModel.GetRenameDecisions().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ApplyOrCancel_RequestsCloseWithResult(bool apply)
    {
        var viewModel = Create(new([_accountProposal], [], [], []));
        bool? result = null;
        viewModel.CloseRequested += (_, applied) => result = applied;

        (apply ? viewModel.ApplyCommand : viewModel.CancelCommand).Execute(null);

        result.ShouldBe(apply);
    }

    [Fact]
    public void GetNewDecisions_EditedRows_ReturnsDecisionPerRepositoryId()
    {
        var viewModel = Create(new([_accountProposal, _standaloneProposal], [], [], []));
        viewModel.NewRows[0].Branch = "develop";
        viewModel.NewRows[1].Choice = NewRepositoryChoice.Skip;

        viewModel.GetNewDecisions().ShouldBe(new Dictionary<long, NewRepositoryDecision>
        {
            [10] = NewRepositoryDecision.Add("develop"),
            [20] = NewRepositoryDecision.Skip,
        });
    }

    /// <summary>
    /// Creates the ViewModel over <paramref name="review"/>.
    /// </summary>
    /// <param name="review">The findings.</param>
    /// <returns>The ViewModel.</returns>
    private static DiscoveryReviewViewModel Create(DiscoveryReview review) => new(review);

    /// <summary>
    /// Creates a public GitHub repository with a wiki.
    /// </summary>
    /// <param name="id">The id.</param>
    /// <param name="owner">The owner login.</param>
    /// <param name="name">The name.</param>
    /// <returns>The repository.</returns>
    private static GitHubRepository Repo(long id, string owner, string name) =>
        new(id, owner, name, false, false, false, true, "main", $"https://github.com/{owner}/{name}");
}
