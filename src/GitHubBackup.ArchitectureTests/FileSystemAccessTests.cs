using System.Reflection;
using NetArchTest.Rules;

namespace GitHubBackup.ArchitectureTests;

/// <summary>
/// Verifies that <c>GitHubBackup.Core</c> and <c>GitHubBackup.Infrastructure</c> reach the disk only through
/// <see cref="System.IO.Abstractions.IFileSystem"/>.
/// </summary>
public sealed class FileSystemAccessTests
{
    /// <summary>
    /// Gets the assemblies the rule applies to.
    /// </summary>
    public static TheoryData<string> Layers => [LayerRules.Core.GetName().Name!, LayerRules.Infrastructure.GetName().Name!];

    [Theory]
    [MemberData(nameof(Layers))]
    public void Types_DirectFileSystemTypes_AreNotUsed(string assemblyName)
    {
        var result = Types.InAssembly(Assembly.Load(new AssemblyName(assemblyName)))
            .ShouldNot()
            .HaveDependencyOnAny([.. LayerRules.DirectFileSystemTypes])
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }
}
