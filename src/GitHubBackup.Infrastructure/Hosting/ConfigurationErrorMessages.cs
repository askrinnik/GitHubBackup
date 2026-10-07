using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Hosting;

/// <summary>
/// Turns the exceptions a host throws for invalid configuration into one message per error.
/// </summary>
/// <remarks>
/// The messages come from the validators and the configuration providers, which name the key or the file but not
/// the value; the token is never bound to an options object, so it cannot appear in them.
/// </remarks>
public static class ConfigurationErrorMessages
{
    /// <summary>
    /// Extracts the configuration errors described by <paramref name="exception"/>.
    /// </summary>
    /// <param name="exception">An exception thrown while the host is built or started.</param>
    /// <param name="messages">One message per error, or an empty list when the exception is not a configuration error.</param>
    /// <returns><see langword="true"/> when <paramref name="exception"/> reports invalid configuration.</returns>
    public static bool TryGet(Exception exception, out IReadOnlyList<string> messages)
    {
        ArgumentNullException.ThrowIfNull(exception);

        messages = exception switch
        {
            OptionsValidationException validation => [.. validation.Failures],
            AggregateException aggregate when aggregate.InnerExceptions.All(inner => inner is OptionsValidationException) =>
                [.. aggregate.InnerExceptions.Cast<OptionsValidationException>().SelectMany(inner => inner.Failures)],
            FileNotFoundException or InvalidDataException => [JoinMessages(exception)],
            _ => [],
        };

        return messages.Count > 0;
    }

    /// <summary>
    /// Joins the message of <paramref name="exception"/> with those of its inner exceptions, so that a JSON syntax
    /// error reports both the file and the line.
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
