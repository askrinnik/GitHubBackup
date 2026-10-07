using System.Reflection;
using GitHubBackup.ArchitectureTests.Fixtures;
using NetArchTest.Rules;

namespace GitHubBackup.ArchitectureTests;

/// <summary>
/// Verifies that the layer rules fail on a violation and stay silent on allowed dependencies.
/// </summary>
public sealed class RuleDetectionTests
{
    [Fact]
    public void Types_FixtureDependingOnHost_ViolatesNamespaceRule()
    {
        var result = Types.InAssembly(typeof(HostDependentFixture).Assembly)
            .That()
            .ResideInNamespace(typeof(HostDependentFixture).Namespace!)
            .ShouldNot()
            .HaveDependencyOnAny([.. LayerRules.CoreForbiddenNamespaces])
            .GetResult();

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(HostDependentFixture).FullName!);
    }

    [Fact]
    public void Find_FixtureAssembly_ReportsHostAssembly()
    {
        var found = ForbiddenReferenceFinder.Find(typeof(HostDependentFixture).Assembly, LayerRules.CoreForbiddenAssemblies);

        found.ShouldContain("GitHubBackup.App");
    }

    [Theory]
    [InlineData("GitHubBackup.Infrastructure")]
    [InlineData("Octokit")]
    [InlineData("Microsoft.EntityFrameworkCore.Sqlite")]
    [InlineData("Microsoft.Data.Sqlite.Core")]
    [InlineData("SQLitePCLRaw.core")]
    [InlineData("Serilog")]
    [InlineData("Serilog.Sinks.File")]
    [InlineData("PresentationFramework")]
    public void Find_ForbiddenReference_IsReported(string referenceName)
    {
        var found = ForbiddenReferenceFinder.Find([new AssemblyName(referenceName)], LayerRules.CoreForbiddenAssemblies);

        found.ShouldBe([referenceName]);
    }

    [Theory]
    [InlineData("Microsoft.Extensions.Options")]
    [InlineData("Microsoft.Extensions.Logging.Abstractions")]
    [InlineData("System.IO.Abstractions")]
    [InlineData("System.Runtime")]
    [InlineData("GitHubBackup.Core")]
    [InlineData("SerilogX")]
    public void Find_AllowedReference_IsNotReported(string referenceName)
    {
        var found = ForbiddenReferenceFinder.Find([new AssemblyName(referenceName)], LayerRules.CoreForbiddenAssemblies);

        found.ShouldBeEmpty();
    }
}
