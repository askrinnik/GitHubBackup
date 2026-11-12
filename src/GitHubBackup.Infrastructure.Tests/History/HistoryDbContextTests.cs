using GitHubBackup.Infrastructure.History;
using Microsoft.EntityFrameworkCore;

namespace GitHubBackup.Infrastructure.Tests.History;

/// <summary>
/// Tests the model of <see cref="HistoryDbContext"/> against the migrations, without opening a database.
/// </summary>
public sealed class HistoryDbContextTests
{
    [Fact]
    public void HasPendingModelChanges_CurrentModel_ReturnsFalse()
    {
        using var context = new HistoryDesignTimeDbContextFactory().CreateDbContext([]);

        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public void Model_Columns_HoldNoCredentials()
    {
        using var context = new HistoryDesignTimeDbContextFactory().CreateDbContext([]);

        var columns = context.Model.GetEntityTypes()
            .SelectMany(entityType => entityType.GetProperties())
            .Select(property => property.GetColumnName())
            .ToList();

        columns.ShouldNotBeEmpty();
        columns.ShouldAllBe(column =>
            !column.Contains("Token", StringComparison.OrdinalIgnoreCase)
            && !column.Contains("Password", StringComparison.OrdinalIgnoreCase)
            && !column.Contains("Secret", StringComparison.OrdinalIgnoreCase)
            && !column.Contains("Credential", StringComparison.OrdinalIgnoreCase)
            && !column.Contains("Url", StringComparison.OrdinalIgnoreCase));
    }
}
