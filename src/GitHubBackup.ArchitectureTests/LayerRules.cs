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

    /// <summary>The configuration system: <c>Core</c> receives bound options and never reads configuration itself.</summary>
    private const string _configuration = "Microsoft.Extensions.Configuration";

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
    /// Gets the <c>Cli</c> assembly.
    /// </summary>
    public static Assembly Cli { get; } = Assembly.Load(new AssemblyName(_cli));

    /// <summary>
    /// Gets the <c>App</c> assembly.
    /// </summary>
    public static Assembly App { get; } = Assembly.Load(new AssemblyName(_app));

    /// <summary>
    /// Gets the namespace and assembly name of Octokit, which only <c>Infrastructure</c> uses, and only in types the
    /// hosts cannot see, so the hosts depend on <c>IGitHubApi</c> alone.
    /// </summary>
    public static string Octokit { get; } = "Octokit";

    /// <summary>
    /// Gets the namespaces whose types <c>Core</c> must not use: the outer layers, the I/O libraries and the
    /// configuration system.
    /// </summary>
    public static IReadOnlyList<string> CoreForbiddenNamespaces { get; } =
        [_infrastructure, .. _hostNamespaces, .. _ioLibraryNamespaces, _configuration];

    /// <summary>
    /// Gets the assemblies <c>Core</c> must not reference directly: the outer layers, the I/O libraries, the
    /// configuration system and WPF.
    /// </summary>
    public static IReadOnlyList<string> CoreForbiddenAssemblies { get; } =
        [_infrastructure, .. _hostNamespaces, .. _ioLibraryAssemblies, _configuration, .. _wpfAssemblies];

    /// <summary>
    /// Gets the <c>System.IO</c> types that touch the disk directly; <c>Core</c> and <c>Infrastructure</c> use
    /// <see cref="System.IO.Abstractions.IFileSystem"/> instead, so tests can replace the disk. <see cref="Path"/>
    /// only computes strings and is allowed.
    /// </summary>
    public static IReadOnlyList<string> DirectFileSystemTypes { get; } =
    [
        typeof(File).FullName!,
        typeof(Directory).FullName!,
        typeof(FileInfo).FullName!,
        typeof(DirectoryInfo).FullName!,
        typeof(FileStream).FullName!,
    ];

    /// <summary>
    /// Gets the types that start an operating-system process; only the process runner of <c>Infrastructure</c> uses
    /// them, so every external tool runs with its arguments as a list, its output logged and secrets masked.
    /// </summary>
    public static IReadOnlyList<string> ProcessTypes { get; } =
    [
        typeof(System.Diagnostics.Process).FullName!,
        typeof(System.Diagnostics.ProcessStartInfo).FullName!,
    ];

    /// <summary>
    /// Gets the full name of the only <c>Infrastructure</c> type allowed to use <see cref="ProcessTypes"/>.
    /// </summary>
    public static string ProcessRunnerTypeName { get; } = _infrastructure + ".Processes.ProcessRunner";

    /// <summary>
    /// Gets the namespaces whose types <c>Infrastructure</c> must not use: the hosts.
    /// </summary>
    public static IReadOnlyList<string> InfrastructureForbiddenNamespaces { get; } = _hostNamespaces;

    /// <summary>
    /// Gets the assemblies <c>Infrastructure</c> must not reference directly: the hosts.
    /// </summary>
    public static IReadOnlyList<string> InfrastructureForbiddenAssemblies { get; } = _hostNamespaces;
}
