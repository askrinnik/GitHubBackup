using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using GitHubBackup.TestUtilities;
using GitHubBackup.TestUtilities.Git;

namespace GitHubBackup.App.UITests;

/// <summary>
/// Runs a backup of one repository from the main window and checks that the rows of the live log and of the summary
/// table keep the height of one line of text.
/// </summary>
/// <remarks>git and 7-Zip run for real against a local bare repository.</remarks>
public sealed class RunRowsTests : IDisposable
{
    /// <summary>The largest row height, as a multiple of the height of its text, that counts as one compact line.</summary>
    private const double _maxRowToTextRatio = 1.6;

    private const string _owner = "octo";
    private const string _name = "alpha";

    private readonly LocalGitSandbox _sandbox = new();
    private readonly GitHubApiStub _api = new();
    private readonly AppInstallation _installation;
    private AppSession? _session;

    /// <summary>
    /// Creates the installation with one standalone repository that the API serves and whose remote is a local bare
    /// repository.
    /// </summary>
    public RunRowsTests()
    {
        _installation = new(_api.Url, Path.Combine(_sandbox.RootPath, "remotes"));
        _api.GivenCurrentUserRepositories(_api.GivenRepository(1, _owner, _name));
        _installation.NewBackupConfig().WithRepository(_owner, _name, 1).WriteTo(_installation.BackupConfigPath);
    }

    [Fact]
    public async Task BackupAll_AfterRun_LogAndSummaryRowsAreOneLineHigh()
    {
        await _sandbox.CreateRemoteAsync(_owner + "/" + _name, TestContext.Current.CancellationToken);
        var session = _session = AppSession.Start(_installation);
        session.WaitFor("RepositoryTree");

        session.Invoke("BackupAllButton");
        session.WaitFor("RunSummaryTotals", UiTimeouts.Run);

        ShouldBeOneLineHigh(FirstRow(session.WaitFor("RunLog"), ControlType.ListItem));
        ShouldBeOneLineHigh(FirstRow(session.WaitFor("RunSummaryTable"), ControlType.DataItem));
    }

    /// <summary>
    /// Stops the application, then deletes the installation and the sandbox and stops the API stub.
    /// </summary>
    public void Dispose()
    {
        _session?.Dispose();
        _installation.Dispose();
        _sandbox.Dispose();
        _api.Dispose();
    }

    /// <summary>
    /// Waits for the first row of <paramref name="list"/> and returns it.
    /// </summary>
    /// <param name="list">The list or table.</param>
    /// <param name="rowType">The control type of its rows.</param>
    /// <returns>The first row.</returns>
    private static AutomationElement FirstRow(AutomationElement list, ControlType rowType)
    {
        AutomationElement? row = null;
        AppSession.WaitUntil(
            () => (row = list.FindFirstDescendant(condition => condition.ByControlType(rowType))) is not null,
            $"a row of {list.AutomationId}");
        return row!;
    }

    /// <summary>
    /// Checks that <paramref name="row"/> is at most <see cref="_maxRowToTextRatio"/> times as high as its first text.
    /// </summary>
    /// <param name="row">The row.</param>
    private static void ShouldBeOneLineHigh(AutomationElement row)
    {
        var text = row.FindFirstDescendant(condition => condition.ByControlType(ControlType.Text)).ShouldNotBeNull();

        ((double)row.BoundingRectangle.Height).ShouldBeLessThanOrEqualTo(text.BoundingRectangle.Height * _maxRowToTextRatio);
    }
}
