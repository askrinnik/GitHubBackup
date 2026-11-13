using NetArchTest.Rules;

namespace GitHubBackup.ArchitectureTests;

/// <summary>
/// Verifies that the ViewModels of <c>GitHubBackup.App</c> use no WPF UI type.
/// </summary>
public sealed class AppViewModelDependencyTests
{
    [Fact]
    public void Types_ViewModels_DoNotUseWpfUiTypes()
    {
        var result = Types.InAssembly(LayerRules.App)
            .That()
            .ResideInNamespace(LayerRules.AppViewModelsNamespace)
            .ShouldNot()
            .HaveDependencyOnAny([.. LayerRules.ViewModelForbiddenWpfTypes])
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }
}
