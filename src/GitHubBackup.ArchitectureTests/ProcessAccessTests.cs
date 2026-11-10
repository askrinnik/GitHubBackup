using NetArchTest.Rules;

namespace GitHubBackup.ArchitectureTests;

/// <summary>
/// Verifies that processes are started only by the process runner of <c>GitHubBackup.Infrastructure</c>.
/// </summary>
public sealed class ProcessAccessTests
{
    [Fact]
    public void CoreTypes_ProcessTypes_AreNotUsed()
    {
        var result = Types.InAssembly(LayerRules.Core)
            .ShouldNot()
            .HaveDependencyOnAny([.. LayerRules.ProcessTypes])
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void InfrastructureTypes_ProcessTypes_AreUsedOnlyByProcessRunner()
    {
        var result = Types.InAssembly(LayerRules.Infrastructure)
            .ShouldNot()
            .HaveDependencyOnAny([.. LayerRules.ProcessTypes])
            .GetResult();

        var users = result.FailingTypeNames ?? [];
        users.ShouldContain(LayerRules.ProcessRunnerTypeName);
        users.Where(name => !IsProcessRunnerOrNested(name)).ShouldBeEmpty();
    }

    /// <summary>
    /// Returns whether <paramref name="typeName"/> names the process runner or a type the compiler nests in it for
    /// its async methods and lambdas.
    /// </summary>
    /// <param name="typeName">The full name of a type, as the rule result reports it.</param>
    /// <returns><see langword="true"/> for the process runner and its nested types.</returns>
    private static bool IsProcessRunnerOrNested(string typeName) =>
        typeName == LayerRules.ProcessRunnerTypeName
        || typeName.StartsWith(LayerRules.ProcessRunnerTypeName + "+", StringComparison.Ordinal)
        || typeName.StartsWith(LayerRules.ProcessRunnerTypeName + "/", StringComparison.Ordinal);
}
