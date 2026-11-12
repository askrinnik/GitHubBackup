using System.IO.Abstractions;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Configuration;
using GitHubBackup.Infrastructure.History;
using GitHubBackup.TestUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.IntegrationTests.History;

/// <summary>
/// Places a run history database at <c>data\history.db</c> in a unique temp folder and deletes the folder on dispose.
/// </summary>
internal sealed class TemporaryHistoryDatabase : IDisposable
{
    /// <summary>
    /// Creates the temp folder; the database itself does not exist until a test creates it.
    /// </summary>
    public TemporaryHistoryDatabase()
    {
        FolderPath = TemporaryFolder.Create();
        DatabasePath = Path.Combine(FolderPath, "data", "history.db");
        ContextFactory = new ContextFactoryStub(DatabasePath);
    }

    /// <summary>Gets the absolute path of the temp folder.</summary>
    public string FolderPath { get; }

    /// <summary>Gets the absolute path of the database file.</summary>
    public string DatabasePath { get; }

    /// <summary>Gets the factory of contexts over the database.</summary>
    public IDbContextFactory<HistoryDbContext> ContextFactory { get; }

    /// <summary>
    /// Creates an initializer over the database.
    /// </summary>
    /// <param name="logger">The logger, or <see langword="null"/> for none.</param>
    /// <returns>The initializer.</returns>
    public HistoryDatabaseInitializer CreateInitializer(ILogger<HistoryDatabaseInitializer>? logger = null) =>
        new(ContextFactory, CreateOptions(), new FileSystem(), logger ?? NullLogger<HistoryDatabaseInitializer>.Instance);

    /// <summary>
    /// Creates the database with the current schema and returns a store over it.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <param name="secretMasker">The masker of error texts, or <see langword="null"/> for one without secrets.</param>
    /// <returns>The store.</returns>
    public async Task<SqliteRunHistoryStore> CreateMigratedStoreAsync(
        CancellationToken cancellationToken, ISecretMasker? secretMasker = null)
    {
        await CreateInitializer().StartAsync(cancellationToken);
        return new(ContextFactory, CreateOptions(), secretMasker ?? new SecretMasker());
    }

    /// <summary>
    /// Runs a query that returns one value, bypassing EF Core, to see what the database stores.
    /// </summary>
    /// <param name="sql">The query.</param>
    /// <param name="cancellationToken">The token that cancels the operation.</param>
    /// <returns>The value of the first column of the first row.</returns>
    public async Task<object?> QueryScalarAsync(string sql, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(HistoryConnectionString.Create(DatabasePath));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    /// <summary>
    /// Deletes the temp folder with the database.
    /// </summary>
    public void Dispose() => TemporaryFolder.Delete(FolderPath);

    /// <summary>
    /// Creates the settings that point at the database.
    /// </summary>
    /// <returns>The settings.</returns>
    private IOptions<HistoryOptions> CreateOptions() => Options.Create(new HistoryOptions { DatabasePath = DatabasePath });

    /// <summary>
    /// Creates contexts over one database file, as the factory registered in the host does.
    /// </summary>
    /// <param name="databasePath">The path of the database file.</param>
    private sealed class ContextFactoryStub(string databasePath) : IDbContextFactory<HistoryDbContext>
    {
        /// <inheritdoc />
        public HistoryDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<HistoryDbContext>()
                .UseSqlite(HistoryConnectionString.Create(databasePath))
                .Options);
    }
}
