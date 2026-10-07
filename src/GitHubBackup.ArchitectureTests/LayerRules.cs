using System.Reflection;

namespace GitHubBackup.ArchitectureTests;

/// <summary>
/// The single description of the layer rule: <c>Cli</c> and <c>App</c> depend on <c>Infrastructure</c>, which depends on <c>Core</c>, and nothing points outward.
/// </summary>
public static class LayerRules
{
    private const string _core = "GitHubBackup.Core";
    private const string _infrastructure = "GitHubBackup.Infrastructure";
    private const string _cli = "GitHubBackup.Cli";
    private const string _app = "GitHubBackup.App";

    private static readonly string[] _hostNamespaces = [_cli, _app];

    private static readonly string[] _ioLibraryNamespaces =
    [
        "Octokit",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.Sqlite",
        "SQLitePCL",
        "Serilog",
    ];

    private static readonly string[] _ioLibraryAssemblies =
    [
        "Octokit",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.Data.Sqlite",
        "SQLitePCLRaw",
        "Serilog",
    ];

    private static readonly string[] _wpfAssemblies = ["PresentationFramework", "PresentationCore", "WindowsBase", "System.Xaml"];

    /// <summary>
    /// Gets the <c>Core</c> assembly.
    /// </summary>
    public static Assembly Core { get; } = Assembly.Load(new AssemblyName(_core));

    /// <summary>
    /// Gets the <c>Infrastructure</c> assembly.
    /// </summary>
    public static Assembly Infrastructure { get; } = Assembly.Load(new AssemblyName(_infrastructure));

    /// <summary>
    /// Gets the namespaces whose types <c>Core</c> must not use: the outer layers and the I/O libraries.
    /// </summary>
    public static IReadOnlyList<string> CoreForbiddenNamespaces { get; } = [_infrastructure, .. _hostNamespaces, .. _ioLibraryNamespaces];

    /// <summary>
    /// Gets the assemblies <c>Core</c> must not reference directly: the outer layers, the I/O libraries and WPF.
    /// </summary>
    public static IReadOnlyList<string> CoreForbiddenAssemblies { get; } = [_infrastructure, .. _hostNamespaces, .. _ioLibraryAssemblies, .. _wpfAssemblies];

    /// <summary>
    /// Gets the namespaces whose types <c>Infrastructure</c> must not use: the hosts.
    /// </summary>
    public static IReadOnlyList<string> InfrastructureForbiddenNamespaces { get; } = _hostNamespaces;

    /// <summary>
    /// Gets the assemblies <c>Infrastructure</c> must not reference directly: the hosts.
    /// </summary>
    public static IReadOnlyList<string> InfrastructureForbiddenAssemblies { get; } = _hostNamespaces;
}
