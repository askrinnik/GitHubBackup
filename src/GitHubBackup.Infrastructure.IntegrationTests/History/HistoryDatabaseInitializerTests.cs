using System.Globalization;
using GitHubBackup.Core;
using GitHubBackup.Core.History;
using GitHubBackup.Core.Runs;
using GitHubBackup.Infrastructure.History;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace GitHubBackup.Infrastructure.IntegrationTests.History;

/// <summary>
/// Tests <see cref="HistoryDatabaseInitializer"/> against real SQLite database files in a temp folder.
/// </summary>
public sealed class HistoryDatabaseInitializerTests : IDisposable
{
    private const int _readyEventId = 1;

    private readonly TemporaryHistoryDatabase _database = new();

    private readonly FakeLogger<HistoryDatabaseInitializer> _logger = new();

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task StartAsync_NoDataFolder_CreatesFolderAndDatabaseWithAllMigrations()
    {
        var migrations = AllMigrations();

        await _database.CreateInitializer(_logger).StartAsync(CancellationToken);

        File.Exists(_database.DatabasePath).ShouldBeTrue();
        (await _database.QueryScalarAsync("SELECT COUNT(*) FROM __EFMigrationsHistory", CancellationToken))
            .ShouldBe((long)migrations.Count);
        (await _database.QueryScalarAsync("SELECT COUNT(*) FROM Runs", CancellationToken)).ShouldBe(0L);
        var record = ReadyRecord();
        record.Level.ShouldBe(LogLevel.Information);
        record.GetStructuredStateValue("Path").ShouldBe(_database.DatabasePath);
        record.GetStructuredStateValue("AppliedMigrations").ShouldBe(migrations.Count.ToString(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task StartAsync_DatabaseUpToDate_KeepsRunsAndAppliesNothing()
    {
        var store = await _database.CreateMigratedStoreAsync(CancellationToken);
        var startedAt = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.FromHours(2));
        var run = new RunHistoryEntry(Guid.NewGuid(), startedAt, startedAt.AddMinutes(1), RunMode.Silent, false, ExitCode.Success);
        await store.AddAsync(run, CancellationToken);

        await _database.CreateInitializer(_logger).StartAsync(CancellationToken);

        (await store.GetAsync(run.RunId, CancellationToken)).ShouldNotBeNull();
        (await _database.QueryScalarAsync("SELECT COUNT(*) FROM __EFMigrationsHistory", CancellationToken))
            .ShouldBe((long)AllMigrations().Count);
        ReadyRecord().GetStructuredStateValue("AppliedMigrations").ShouldBe("0");
    }

    [Fact]
    public async Task StartAsync_PathIsExistingFolder_ThrowsRunHistoryExceptionNamingPath()
    {
        Directory.CreateDirectory(_database.DatabasePath);

        var exception = await Should.ThrowAsync<RunHistoryException>(
            () => _database.CreateInitializer(_logger).StartAsync(CancellationToken));

        exception.DatabasePath.ShouldBe(_database.DatabasePath);
        exception.Message.ShouldContain(_database.DatabasePath);
    }

    [Fact]
    public async Task StartAsync_FileIsNotDatabase_ThrowsRunHistoryExceptionNamingPath()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_database.DatabasePath)!);
        await File.WriteAllTextAsync(
            _database.DatabasePath, "This file is not an SQLite database, but it is long enough to have a header.", CancellationToken);

        var exception = await Should.ThrowAsync<RunHistoryException>(
            () => _database.CreateInitializer(_logger).StartAsync(CancellationToken));

        exception.Message.ShouldContain(_database.DatabasePath);
    }

    /// <summary>
    /// Deletes the temp folder with the database.
    /// </summary>
    public void Dispose() => _database.Dispose();

    /// <summary>
    /// Returns the identifiers of every migration in the assembly.
    /// </summary>
    /// <returns>The migration identifiers.</returns>
    private List<string> AllMigrations()
    {
        using var context = _database.ContextFactory.CreateDbContext();
        return [.. context.Database.GetMigrations()];
    }

    /// <summary>
    /// Returns the only entry that reports the database as ready.
    /// </summary>
    /// <returns>The log record.</returns>
    private FakeLogRecord ReadyRecord() =>
        _logger.Collector.GetSnapshot().Where(record => record.Id.Id == _readyEventId).ShouldHaveSingleItem();
}
