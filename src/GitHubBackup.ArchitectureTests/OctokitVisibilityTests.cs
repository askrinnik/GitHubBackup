using System.Reflection;
using NetArchTest.Rules;

namespace GitHubBackup.ArchitectureTests;

/// <summary>
/// Verifies that Octokit stays an implementation detail of <c>GitHubBackup.Infrastructure</c>.
/// </summary>
public sealed class OctokitVisibilityTests
{
    /// <summary>
    /// Gets the names of the host assemblies.
    /// </summary>
    public static TheoryData<string> Hosts => [nameof(LayerRules.Cli), nameof(LayerRules.App)];

    [Fact]
    public void InfrastructureTypes_DependingOnOctokit_AreNotPublic()
    {
        var octokitTypes = Types.InAssembly(LayerRules.Infrastructure).That().HaveDependencyOn(LayerRules.Octokit);

        var selected = octokitTypes.GetTypes();
        var result = octokitTypes.ShouldNot().BePublic().GetResult();

        selected.ShouldNotBeEmpty("the rule must select the types that use Octokit");
        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void HostReferences_Octokit_AreAbsent(string host)
    {
        var found = ForbiddenReferenceFinder.Find(HostAssembly(host), [LayerRules.Octokit]);

        found.ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(Hosts))]
    public void HostTypes_OctokitNamespace_IsNotUsed(string host)
    {
        var result = Types.InAssembly(HostAssembly(host))
            .ShouldNot()
            .HaveDependencyOn(LayerRules.Octokit)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    /// <summary>
    /// Returns the host assembly with the given name.
    /// </summary>
    /// <param name="host">The name of a property of <see cref="LayerRules"/> that holds a host assembly.</param>
    /// <returns>The assembly.</returns>
    private static Assembly HostAssembly(string host) => host == nameof(LayerRules.Cli) ? LayerRules.Cli : LayerRules.App;
}
