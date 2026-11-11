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
    /// <param name="isInputRedirected">Whether the console reports redirected standard input.</param>
    /// <returns>The outputs.</returns>
    public static CliConsole Create(TextWriter? error = null, bool isInputRedirected = false) =>
        new(CreateOutput(), error ?? new StringWriter(), isInputRedirected);

    /// <summary>
    /// Creates a <see cref="TestConsole"/> wide enough that a summary table does not wrap.
    /// </summary>
    /// <returns>The console.</returns>
    public static TestConsole CreateOutput() => new TestConsole().Width(200);

    /// <summary>
    /// Creates a wide <see cref="TestConsole"/> that supports prompts, whose input the test fills.
    /// </summary>
    /// <returns>The console.</returns>
    public static TestConsole CreateInteractiveOutput()
    {
        var output = CreateOutput();
        output.Interactive();
        return output;
    }
}
