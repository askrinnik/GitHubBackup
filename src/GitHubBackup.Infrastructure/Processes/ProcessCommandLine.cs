using System.Text;

namespace GitHubBackup.Infrastructure.Processes;

/// <summary>
/// Builds the command line of a process as text for the log.
/// </summary>
/// <remarks>
/// The text is never executed: the arguments reach the process as a list. It quotes the way the Windows C runtime
/// parses a command line, so a logged command can be copied into a terminal and splits into the same arguments.
/// </remarks>
internal static class ProcessCommandLine
{
    /// <summary>
    /// Returns the executable and its arguments joined by spaces, each quoted when it is empty or contains white
    /// space or a quote.
    /// </summary>
    /// <param name="fileName">The path of the executable.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The command line.</returns>
    public static string Format(string fileName, IEnumerable<string> arguments)
    {
        var builder = new StringBuilder();
        AppendQuoted(builder, fileName);
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            AppendQuoted(builder, argument);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Appends <paramref name="value"/>, quoted when it needs quotes; inside quotes a quote is escaped with a
    /// backslash and the backslashes that precede a quote are doubled.
    /// </summary>
    /// <param name="builder">The builder of the command line.</param>
    /// <param name="value">The executable or argument.</param>
    private static void AppendQuoted(StringBuilder builder, string value)
    {
        if (value.Length > 0 && !value.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            builder.Append(value);
            return;
        }

        builder.Append('"');
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                builder.Append('\\', (backslashes * 2) + 1);
            }
            else
            {
                builder.Append('\\', backslashes);
            }

            builder.Append(character);
            backslashes = 0;
        }

        builder.Append('\\', backslashes * 2).Append('"');
    }
}
