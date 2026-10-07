using NetArchTest.Rules;

namespace GitHubBackup.ArchitectureTests;

/// <summary>
/// Verifies that <c>GitHubBackup.Core</c> depends on no outer layer and no I/O library.
/// </summary>
public sealed class CoreDependencyTests
{
    /// <summary>
    /// Gets the namespaces <c>Core</c> must not use.
    /// </summary>
    public static TheoryData<string> ForbiddenNamespaces => [.. LayerRules.CoreForbiddenNamespaces];

    /// <summary>
    /// Gets the assemblies <c>Core</c> must not reference.
    /// </summary>
    public static TheoryData<string> ForbiddenAssemblies => [.. LayerRules.CoreForbiddenAssemblies];

    [Theory]
    [MemberData(nameof(ForbiddenNamespaces))]
    public void Types_ForbiddenNamespace_AreNotUsed(string forbiddenNamespace)
    {
        var result = Types.InAssembly(LayerRules.Core)
            .ShouldNot()
            .HaveDependencyOn(forbiddenNamespace)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [MemberData(nameof(ForbiddenAssemblies))]
    public void References_ForbiddenAssembly_AreAbsent(string forbiddenAssembly)
    {
        var found = ForbiddenReferenceFinder.Find(LayerRules.Core, [forbiddenAssembly]);

        found.ShouldBeEmpty();
    }
}
