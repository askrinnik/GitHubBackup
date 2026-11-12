using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GitHubBackup.Infrastructure.History;

/// <summary>
/// Creates the <see cref="HistoryDbContext"/> for the <c>dotnet ef</c> tool, which cannot start the host of the
/// application; the application itself never uses this factory.
/// </summary>
internal sealed class HistoryDesignTimeDbContextFactory : IDesignTimeDbContextFactory<HistoryDbContext>
{
    /// <summary>The database file the tool connects to; adding a migration never opens it.</summary>
    private const string _designTimeDatabasePath = "history.db";

    /// <inheritdoc />
    public HistoryDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<HistoryDbContext>()
            .UseSqlite(HistoryConnectionString.Create(_designTimeDatabasePath))
            .Options);
}
