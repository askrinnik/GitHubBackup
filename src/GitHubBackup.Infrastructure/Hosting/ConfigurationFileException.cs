namespace GitHubBackup.Infrastructure.Hosting;

/// <summary>
/// Reports that a configuration file the host reads at start-up (<c>appsettings.json</c> or the user secrets file)
/// is missing or unreadable.
/// </summary>
/// <remarks>
/// The host throws this type only for its own configuration inputs, so <see cref="ConfigurationErrorMessages"/> can
/// tell them apart from a <see cref="FileNotFoundException"/> or an <see cref="InvalidDataException"/> raised by any
/// other code.
/// </remarks>
public sealed class ConfigurationFileException : Exception
{
    /// <summary>
    /// Initializes a new instance with a message that describes the error.
    /// </summary>
    /// <param name="message">The description of the error.</param>
    public ConfigurationFileException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance for the failure to load a configuration file.
    /// </summary>
    /// <remarks>
    /// The message joins the message of <paramref name="loadFailure"/> with those of its inner exceptions, so that a
    /// JSON syntax error reports both the file and the line.
    /// </remarks>
    /// <param name="loadFailure">The exception thrown while the file was loaded.</param>
    public ConfigurationFileException(Exception loadFailure)
        : base(JoinMessages(loadFailure), loadFailure)
    {
    }

    /// <summary>
    /// Initializes a new instance with a message and the exception that caused it.
    /// </summary>
    /// <param name="message">The description of the error.</param>
    /// <param name="innerException">The exception that caused the error.</param>
    public ConfigurationFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Joins the message of <paramref name="exception"/> with those of its inner exceptions.
    /// </summary>
    /// <param name="exception">The outermost exception.</param>
    /// <returns>The messages separated by spaces.</returns>
    private static string JoinMessages(Exception exception)
    {
        List<string> parts = [];
        for (var current = exception; current is not null; current = current.InnerException)
        {
            parts.Add(current.Message);
        }

        return string.Join(' ', parts);
    }
}
