using Microsoft.Data.Sqlite;

namespace GitHubBackup.Infrastructure.History;

/// <summary>
/// Builds the connection string of the run history database.
/// </summary>
internal static class HistoryConnectionString
{
    /// <summary>
    /// Returns the connection string of the database file at <paramref name="databasePath"/>.
    /// </summary>
    /// <remarks>
    /// Pooling is off: a pooled connection keeps the file open after the context is disposed, which locks the
    /// <c>data</c> folder for as long as the process runs. A run opens the database only a few times, so the pool
    /// saves nothing measurable.
    /// </remarks>
    /// <param name="databasePath">The full path of the database file.</param>
    /// <returns>The connection string.</returns>
    public static string Create(string databasePath) =>
        new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString();
}
