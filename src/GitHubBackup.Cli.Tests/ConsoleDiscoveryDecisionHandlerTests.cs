using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.GitHub;
using Spectre.Console.Testing;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Tests <see cref="ConsoleDiscoveryDecisionHandler"/> over a <see cref="TestConsole"/> with emulated input.
/// </summary>
public sealed class ConsoleDiscoveryDecisionHandlerTests : IDisposable
{
    private const string _archiveFolder = @"C:\Backups\octocat\hello";

    private readonly TestConsole _output = TestCliConsole.CreateInteractiveOutput();
    private readonly StringWriter _error = new();

    [Fact]
    public async Task DecideNewAsync_AccountRepository_ShowsDetailsAndProposedEntry()
    {
        _output.Input.PushKey(ConsoleKey.Enter);

        await CreateHandler().DecideNewAsync(CreateNew(), TestContext.Current.CancellationToken);

        TableLines().ShouldBe(
            """
                                New repository
            ┌─────────────────┬──────────────────────────────────┐
            │ Field           │ Value                            │
            ├─────────────────┼──────────────────────────────────┤
            │ Repository      │ octocat/hello                    │
            │ URL             │ https://github.com/octocat/hello │
            │ Owner           │ octocat                          │
            │ Visibility      │ Public                           │
            │ Fork            │ Yes                              │
            │ Archived        │ No                               │
            │ Default branch  │ main                             │
            │ Wiki            │ Yes                              │
            │ Account         │ octocat                          │
            │ Proposed name   │ hello                            │
            │ Proposed id     │ 42                               │
            │ Proposed branch │ (default)                        │
            │ Archive folder  │ C:\Backups\octocat\hello         │
            └─────────────────┴──────────────────────────────────┘
            """);
    }

    [Fact]
    public async Task DecideNewAsync_Standalone_ShowsStandaloneAccountAndOffersNoAccountExclusion()
    {
        _output.Input.PushKey(ConsoleKey.Enter);

        await CreateHandler().DecideNewAsync(CreateNew(accountLogin: null), TestContext.Current.CancellationToken);

        _output.Output.ShouldContain("│ Account         │ standalone");
        _output.Output.ShouldNotContain("Exclude from account");
        _output.Output.ShouldContain("Exclude globally");
    }

    [Fact]
    public async Task DecideNewAsync_AccountRepository_OffersEveryAction()
    {
        _output.Input.PushKey(ConsoleKey.Enter);

        await CreateHandler().DecideNewAsync(CreateNew(), TestContext.Current.CancellationToken);

        foreach (var label in new[] { "Add", "Add with changes", "Skip this time", "Exclude from account", "Exclude globally" })
        {
            _output.Output.ShouldContain(label);
        }
    }

    [Fact]
    public async Task DecideNewAsync_Enter_AddsWithDefaults()
    {
        _output.Input.PushKey(ConsoleKey.Enter);

        var decision = await CreateHandler().DecideNewAsync(CreateNew(), TestContext.Current.CancellationToken);

        decision.ShouldBe(NewRepositoryDecision.Add());
    }

    [Fact]
    public async Task DecideNewAsync_AddWithChanges_ReturnsTypedBranchAndPath()
    {
        PushSelection(1);
        _output.Input.PushTextWithEnter("develop");
        _output.Input.PushTextWithEnter(@"D:\Other\hello");

        var decision = await CreateHandler().DecideNewAsync(CreateNew(), TestContext.Current.CancellationToken);

        decision.ShouldBe(NewRepositoryDecision.Add("develop", @"D:\Other\hello"));
    }

    [Fact]
    public async Task DecideNewAsync_AddWithChangesEmptyAnswers_ReturnsDefaults()
    {
        PushSelection(1);
        _output.Input.PushKey(ConsoleKey.Enter);
        _output.Input.PushKey(ConsoleKey.Enter);

        var decision = await CreateHandler().DecideNewAsync(CreateNew(), TestContext.Current.CancellationToken);

        decision.ShouldBe(NewRepositoryDecision.Add());
    }

    [Fact]
    public async Task DecideNewAsync_AddWithChangesProposedPath_ReturnsNullPath()
    {
        PushSelection(1);
        _output.Input.PushKey(ConsoleKey.Enter);
        _output.Input.PushTextWithEnter(_archiveFolder);

        var decision = await CreateHandler().DecideNewAsync(CreateNew(), TestContext.Current.CancellationToken);

        decision.ShouldBe(NewRepositoryDecision.Add());
    }

    [Fact]
    public async Task DecideNewAsync_AddWithChangesBranchEqualToDefault_KeepsTheBranch()
    {
        PushSelection(1);
        _output.Input.PushTextWithEnter("main");
        _output.Input.PushKey(ConsoleKey.Enter);

        var decision = await CreateHandler().DecideNewAsync(CreateNew(), TestContext.Current.CancellationToken);

        decision.ShouldBe(NewRepositoryDecision.Add("main"));
    }

    [Theory]
    [InlineData("-D")]
    [InlineData("  -D")]
    public async Task DecideNewAsync_InvalidBranch_AsksAgain(string invalid)
    {
        PushSelection(1);
        _output.Input.PushTextWithEnter(invalid);
        _output.Input.PushTextWithEnter("release");
        _output.Input.PushKey(ConsoleKey.Enter);

        var decision = await CreateHandler().DecideNewAsync(CreateNew(), TestContext.Current.CancellationToken);

        decision.ShouldBe(NewRepositoryDecision.Add("release"));
        _output.Output.ShouldContain("The branch must not start with '-'.");
    }

    [Theory]
    [InlineData("   ", null)]
    [InlineData("  release  ", "release")]
    public async Task DecideNewAsync_BranchWithSurroundingSpaces_IsTrimmed(string typed, string? expected)
    {
        PushSelection(1);
        _output.Input.PushTextWithEnter(typed);
        _output.Input.PushKey(ConsoleKey.Enter);

        var decision = await CreateHandler().DecideNewAsync(CreateNew(), TestContext.Current.CancellationToken);

        decision.ShouldBe(NewRepositoryDecision.Add(expected));
    }

    [Theory]
    [InlineData("relative")]
    [InlineData(@"\\?\C:\Backups")]
    [InlineData(@"\\.\C:\Backups")]
    [InlineData(@"C:\Back|ups")]
    public async Task DecideNewAsync_InvalidPath_AsksAgain(string invalid)
    {
        PushSelection(1);
        _output.Input.PushKey(ConsoleKey.Enter);
        _output.Input.PushTextWithEnter(invalid);
        _output.Input.PushTextWithEnter(@"D:\Other");

        var decision = await CreateHandler().DecideNewAsync(CreateNew(), TestContext.Current.CancellationToken);

        decision.ShouldBe(NewRepositoryDecision.Add(null, @"D:\Other"));
        _output.Output.ShouldContain("The archive folder must be an absolute path");
    }

    [Fact]
    public async Task DecideNewAsync_Skip_ReturnsSkip()
    {
        PushSelection(2);

        var decision = await CreateHandler().DecideNewAsync(CreateNew(), TestContext.Current.CancellationToken);

        decision.ShouldBe(NewRepositoryDecision.Skip);
    }

    [Fact]
    public async Task DecideNewAsync_ExcludeFromAccount_ReturnsAccountExclusion()
    {
        PushSelection(3);

        var decision = await CreateHandler().DecideNewAsync(CreateNew(), TestContext.Current.CancellationToken);

        decision.ShouldBe(NewRepositoryDecision.Exclude(ExclusionScope.Account));
    }

    [Fact]
    public async Task DecideNewAsync_ExcludeGloballyOfAccountRepository_ReturnsGlobalExclusion()
    {
        PushSelection(4);

        var decision = await CreateHandler().DecideNewAsync(CreateNew(), TestContext.Current.CancellationToken);

        decision.ShouldBe(NewRepositoryDecision.Exclude(ExclusionScope.Global));
    }

    [Fact]
    public async Task DecideNewAsync_ExcludeGloballyOfStandalone_ReturnsGlobalExclusion()
    {
        PushSelection(3);

        var decision = await CreateHandler().DecideNewAsync(CreateNew(accountLogin: null), TestContext.Current.CancellationToken);

        decision.ShouldBe(NewRepositoryDecision.Exclude(ExclusionScope.Global));
    }

    [Fact]
    public async Task DecideNewAsync_BracketsInValues_AreShownLiterally()
    {
        _output.Input.PushKey(ConsoleKey.Enter);
        var proposal = CreateNew() with
        {
            Repository = CreateRepository() with { DefaultBranch = "[red]main[/]" },
            ArchiveFolder = @"C:\Backups\[blue]x[/]",
        };

        await CreateHandler().DecideNewAsync(proposal, TestContext.Current.CancellationToken);

        _output.Output.ShouldContain("[red]main[/]");
        _output.Output.ShouldContain(@"C:\Backups\[blue]x[/]");
    }

    [Fact]
    public async Task DecideNewAsync_BracketsInPathPrompt_AreShownLiterally()
    {
        PushSelection(1);
        _output.Input.PushKey(ConsoleKey.Enter);
        _output.Input.PushKey(ConsoleKey.Enter);

        await CreateHandler().DecideNewAsync(CreateNew() with { ArchiveFolder = @"C:\[blue]x[/]" }, TestContext.Current.CancellationToken);

        _output.Output.ShouldContain(@"Archive folder (empty for C:\[blue]x[/]):");
    }

    [Fact]
    public async Task DecideNewAsync_Cancelled_ThrowsWithoutAsking()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => CreateHandler().DecideNewAsync(CreateNew(), cancellation.Token));

        _output.Output.ShouldBeEmpty();
    }

    [Fact]
    public async Task DecideRenameAsync_DifferentFolders_ShowsBothNamesAndFolders()
    {
        _output.Input.PushKey(ConsoleKey.Enter);

        await CreateHandler().DecideRenameAsync(CreateRename(), TestContext.Current.CancellationToken);

        _output.Output.ShouldContain("octocat/old");
        _output.Output.ShouldContain("octocat/hello");
        _output.Output.ShouldContain(@"C:\Backups\octocat\old");
        _output.Output.ShouldContain(@"C:\Backups\octocat\hello");
        _output.Output.ShouldContain("The clone moves to the new name in either case.");
    }

    [Fact]
    public async Task DecideRenameAsync_Enter_KeepsArchiveFolder()
    {
        _output.Input.PushKey(ConsoleKey.Enter);

        var decision = await CreateHandler().DecideRenameAsync(CreateRename(), TestContext.Current.CancellationToken);

        decision.ShouldBe(RenameDecision.KeepArchiveFolder);
        _output.Output.IndexOf("b) Keep", StringComparison.Ordinal)
            .ShouldBeLessThan(_output.Output.IndexOf("a) Rename", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DecideRenameAsync_SecondChoice_RenamesArchiveFolder()
    {
        PushSelection(1);

        var decision = await CreateHandler().DecideRenameAsync(CreateRename(), TestContext.Current.CancellationToken);

        decision.ShouldBe(RenameDecision.RenameArchiveFolder);
    }

    [Fact]
    public async Task DecideRenameAsync_SameFolder_KeepsWithoutQuestion()
    {
        var proposal = CreateRename() with { RenamedArchiveFolder = @"C:\Backups\octocat\old" };

        var decision = await CreateHandler().DecideRenameAsync(proposal, TestContext.Current.CancellationToken);

        decision.ShouldBe(RenameDecision.KeepArchiveFolder);
        _output.Output.ShouldContain("does not depend on the name");
        _output.Output.ShouldNotContain("b) Keep");
    }

    [Fact]
    public async Task DecideRenameAsync_Cancelled_ThrowsWithoutAsking()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => CreateHandler().DecideRenameAsync(CreateRename(), cancellation.Token));

        _output.Output.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReviewAsync_WritesNothing()
    {
        await CreateHandler().ReviewAsync(
            new([CreateNew()], [CreateRename()], [], []), TestContext.Current.CancellationToken);

        _output.Output.ShouldBeEmpty();
    }

    [Fact]
    public async Task ConfirmIdSaveAsync_Interactive_AsksQuestionNamingRepositoryAndId()
    {
        _output.Input.PushKey(ConsoleKey.Enter);

        await CreateHandler().ConfirmIdSaveAsync(new("octocat/[hello]", 42), TestContext.Current.CancellationToken);

        _output.Output.ShouldStartWith("ID is missing for octocat/[hello]; found 42. Save it to backup-config.json? [y/n] (y):");
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("y", true)]
    [InlineData("n", false)]
    public async Task ConfirmIdSaveAsync_Answer_ReturnsWhetherToSave(string answer, bool expected)
    {
        _output.Input.PushTextWithEnter(answer);

        var save = await CreateHandler().ConfirmIdSaveAsync(new("octocat/hello", 42), TestContext.Current.CancellationToken);

        save.ShouldBe(expected);
    }

    [Fact]
    public async Task ConfirmIdSaveAsync_InputRedirected_ReturnsTrueWithoutAskingOrReading()
    {
        var handler = new ConsoleDiscoveryDecisionHandler(new(_output, _error, IsInputRedirected: true));

        var save = await handler.ConfirmIdSaveAsync(new("octocat/hello", 42), TestContext.Current.CancellationToken);

        save.ShouldBeTrue();
        _output.Output.ShouldBeEmpty();
    }

    [Fact]
    public async Task ConfirmIdSaveAsync_Cancelled_ThrowsWithoutAsking()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => CreateHandler().ConfirmIdSaveAsync(new("octocat/hello", 42), cancellation.Token));

        _output.Output.ShouldBeEmpty();
    }

    /// <summary>
    /// Disposes the outputs.
    /// </summary>
    public void Dispose()
    {
        _output.Dispose();
        _error.Dispose();
    }

    /// <summary>
    /// Creates the handler over the test console.
    /// </summary>
    /// <returns>The handler.</returns>
    private ConsoleDiscoveryDecisionHandler CreateHandler() => new(new(_output, _error, IsInputRedirected: false));

    /// <summary>
    /// Returns the output up to the end of the first table, without trailing spaces.
    /// </summary>
    /// <returns>The lines, joined by <see cref="Environment.NewLine"/>.</returns>
    private string TableLines()
    {
        var lines = _output.Output.Split('\n').Select(line => line.TrimEnd()).ToList();
        var end = lines.FindIndex(line => line.StartsWith('└')) + 1;
        return string.Join(Environment.NewLine, lines.Take(end));
    }

    /// <summary>
    /// Emulates moving down <paramref name="steps"/> choices and confirming.
    /// </summary>
    /// <param name="steps">The number of presses of the down arrow.</param>
    private void PushSelection(int steps)
    {
        for (var step = 0; step < steps; step++)
        {
            _output.Input.PushKey(ConsoleKey.DownArrow);
        }

        _output.Input.PushKey(ConsoleKey.Enter);
    }

    /// <summary>
    /// Creates a public repository of an account with a short, bracket-free description.
    /// </summary>
    /// <returns>The repository.</returns>
    private static GitHubRepository CreateRepository() =>
        new(42, "octocat", "hello", IsPrivate: false, IsFork: true, IsArchived: false, HasWiki: true, "main", "https://github.com/octocat/hello");

    /// <summary>
    /// Creates the proposal for a new repository.
    /// </summary>
    /// <param name="accountLogin">The tracked account, or <see langword="null"/> for a standalone repository.</param>
    /// <returns>The proposal.</returns>
    private static NewRepositoryProposal CreateNew(string? accountLogin = "octocat") =>
        new(CreateRepository(), accountLogin, _archiveFolder);

    /// <summary>
    /// Creates the proposal for a repository renamed from <c>octocat/old</c> to <c>octocat/hello</c>.
    /// </summary>
    /// <returns>The proposal.</returns>
    private static RenameProposal CreateRename() =>
        new(
            new(new("octocat", "old", 42, RepositoryStatus.Active, "octocat"), CreateRepository()),
            @"C:\Backups\octocat\old",
            @"C:\Backups\octocat\hello");
}
