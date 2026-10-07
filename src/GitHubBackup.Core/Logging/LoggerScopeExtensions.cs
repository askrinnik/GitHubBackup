using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Logging;

/// <summary>
/// Opens logging scopes that add <see cref="LogProperties.Repository"/> or <see cref="LogProperties.Operation"/> to
/// every entry written inside them.
/// </summary>
/// <remarks>
/// The scope state is a list of name-value pairs, which logging providers turn into separate properties of the entry
/// rather than one text value.
/// </remarks>
public static class LoggerScopeExtensions
{
    /// <param name="logger">The logger that writes the entries.</param>
    extension(ILogger logger)
    {
        /// <summary>
        /// Begins a scope for the repository <paramref name="repository"/>.
        /// </summary>
        /// <param name="repository">The repository, as <c>owner/repo</c>.</param>
        /// <returns>The scope to dispose when the work on the repository ends, or <see langword="null"/>.</returns>
        public IDisposable? BeginRepositoryScope(string repository)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(repository);

            return logger.BeginScope(CreateState(LogProperties.Repository, repository));
        }

        /// <summary>
        /// Begins a scope for the operation <paramref name="operation"/>.
        /// </summary>
        /// <param name="operation">The operation, for example <c>clone</c>.</param>
        /// <returns>The scope to dispose when the operation ends, or <see langword="null"/>.</returns>
        public IDisposable? BeginOperationScope(string operation)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(operation);

            return logger.BeginScope(CreateState(LogProperties.Operation, operation));
        }
    }

    /// <summary>
    /// Creates the scope state that holds one property.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <param name="value">The property value.</param>
    /// <returns>The scope state.</returns>
    private static KeyValuePair<string, object?>[] CreateState(string name, string value) => [new(name, value)];
}
