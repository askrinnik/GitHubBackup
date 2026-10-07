using NetArchTest.Rules;

namespace GitHubBackup.ArchitectureTests;

/// <summary>
/// Verifies that <c>GitHubBackup.Infrastructure</c> depends on neither host.
/// </summary>
public sealed class InfrastructureDependencyTests
{
    /// <summary>
    /// Gets the namespaces <c>Infrastructure</c> must not use.
    /// </summary>
    public static TheoryData<string> ForbiddenNamespaces => [.. LayerRules.InfrastructureForbiddenNamespaces];

    /// <summary>
    /// Gets the assemblies <c>Infrastructure</c> must not reference.
    /// </summary>
    public static TheoryData<string> ForbiddenAssemblies => [.. LayerRules.InfrastructureForbiddenAssemblies];

    [Theory]
    [MemberData(nameof(ForbiddenNamespaces))]
    public void Types_ForbiddenNamespace_AreNotUsed(string forbiddenNamespace)
    {
        var result = Types.InAssembly(LayerRules.Infrastructure)
            .ShouldNot()
            .HaveDependencyOn(forbiddenNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(ForbiddenAssemblies))]
    public void References_ForbiddenAssembly_AreAbsent(string forbiddenAssembly)
    {
        var found = ForbiddenReferenceFinder.Find(LayerRules.Infrastructure, [forbiddenAssembly]);

        found.ShouldBeEmpty();
    }
}
