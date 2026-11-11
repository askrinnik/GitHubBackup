using Spectre.Console.Testing;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Creates <see cref="CliConsole"/> instances that record their output instead of writing to the terminal.
/// </summary>
internal static class TestCliConsole
{
    /// <summary>
    /// Creates outputs over a wide <see cref="TestConsole"/> and <paramref name="error"/>.
    /// </summary>
    /// <param name="error">The error writer, or <see langword="null"/> for a new <see cref="StringWriter"/>.</param>
    /// <returns>The outputs.</returns>
    public static CliConsole Create(TextWriter? error = null) => new(CreateOutput(), error ?? new StringWriter());

    /// <summary>
    /// Creates a <see cref="TestConsole"/> wide enough that a summary table does not wrap.
    /// </summary>
    /// <returns>The console.</returns>
    public static TestConsole CreateOutput() => new TestConsole().Width(200);
}
