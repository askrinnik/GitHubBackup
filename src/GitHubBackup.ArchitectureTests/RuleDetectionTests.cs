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
    public void Types_FixtureCallingFileExists_ViolatesFileSystemRule()
    {
        var result = Types.InAssembly(typeof(DirectFileAccessFixture).Assembly)
            .That()
            .HaveName(nameof(DirectFileAccessFixture))
            .ShouldNot()
            .HaveDependencyOnAny([.. LayerRules.DirectFileSystemTypes])
            .GetResult();

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(DirectFileAccessFixture).FullName!);
    }

    [Fact]
    public void Types_FixtureUsingPathAndFileFlags_SatisfiesFileSystemRule()
    {
        var result = Types.InAssembly(typeof(AllowedFileTypesFixture).Assembly)
            .That()
            .HaveName(nameof(AllowedFileTypesFixture))
            .ShouldNot()
            .HaveDependencyOnAny([.. LayerRules.DirectFileSystemTypes])
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Types_FixtureStartingProcess_ViolatesProcessRule()
    {
        var result = Types.InAssembly(typeof(ProcessStartingFixture).Assembly)
            .That()
            .HaveName(nameof(ProcessStartingFixture))
            .ShouldNot()
            .HaveDependencyOnAny([.. LayerRules.ProcessTypes])
            .GetResult();

        result.IsSuccessful.ShouldBeFalse();
        result.FailingTypeNames.ShouldContain(typeof(ProcessStartingFixture).FullName!);
    }

    [Fact]
    public void Types_FixtureUsingStopwatchAndWindowStyle_SatisfiesProcessRule()
    {
        var result = Types.InAssembly(typeof(AllowedDiagnosticsTypesFixture).Assembly)
            .That()
            .HaveName(nameof(AllowedDiagnosticsTypesFixture))
            .ShouldNot()
            .HaveDependencyOnAny([.. LayerRules.ProcessTypes])
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
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
    [InlineData("Microsoft.Extensions.Configuration.Abstractions")]
    [InlineData("Microsoft.Extensions.Configuration.Binder")]
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
