using GitHubBackup.Infrastructure.History;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.Infrastructure.IntegrationTests.History;

/// <summary>
/// Tests that the shared host creates the run history database through <see cref="HistoryDatabaseInitializer"/>.
/// </summary>
public sealed class HistoryDatabaseHostTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartAsync_DefaultSettings_CreatesDataHistoryDbInContentRoot()
    {
        using var contentRoot = new TemporaryContentRoot("{}");

        using (var host = contentRoot.BuildHost())
        {
            await host.StartAsync(CancellationToken);
            await host.StopAsync(CancellationToken);
        }

        File.Exists(Path.Combine(contentRoot.FolderPath, "data", "history.db")).ShouldBeTrue();
    }
}
