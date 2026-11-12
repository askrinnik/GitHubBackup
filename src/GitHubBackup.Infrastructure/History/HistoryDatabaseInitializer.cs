using System.IO.Abstractions;
using GitHubBackup.Core.History;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.History;

/// <summary>
/// Creates the run history database and brings its schema up to date when the host starts, so that every command
/// finds the database ready and an unusable database stops the run before any work is done.
/// </summary>
/// <param name="contextFactory">Creates the context of the database.</param>
/// <param name="options">The settings with the full path of the database.</param>
/// <param name="fileSystem">Creates the folder of the database.</param>
/// <param name="logger">The logger.</param>
internal sealed class HistoryDatabaseInitializer(
    IDbContextFactory<HistoryDbContext> contextFactory,
    IOptions<HistoryOptions> options,
    IFileSystem fileSystem,
    ILogger<HistoryDatabaseInitializer> logger) : IHostedService
{
    /// <summary>
    /// Creates the folder of the database and applies the migrations the database does not have yet; SQLite creates
    /// the file itself when it is missing.
    /// </summary>
    /// <param name="cancellationToken">The token that cancels the start.</param>
    /// <returns>A task that completes when the database is ready.</returns>
    /// <exception cref="RunHistoryException">The folder cannot be created, or the database cannot be opened or migrated.</exception>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var path = options.Value.DatabasePath;
        try
        {
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                fileSystem.Directory.CreateDirectory(folder);
            }

            await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
            var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
            var pendingCount = pending.Count();
            await context.Database.MigrateAsync(cancellationToken);
            HistoryDatabaseLog.Ready(logger, path, pendingCount);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or UnauthorizedAccessException)
        {
            throw new RunHistoryException(path, exception.Message, exception);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
