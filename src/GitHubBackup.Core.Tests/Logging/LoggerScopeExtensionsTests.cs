using GitHubBackup.Core.Logging;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Core.Tests.Logging;

/// <summary>
/// Tests <see cref="LoggerScopeExtensions"/>.
/// </summary>
public sealed class LoggerScopeExtensionsTests
{
    private readonly ScopeRecordingLogger _logger = new();

    [Fact]
    public void BeginRepositoryScope_Repository_BeginsScopeWithRepositoryProperty()
    {
        using var scope = _logger.BeginRepositoryScope("octocat/hello-world");

        _logger.States.ShouldHaveSingleItem().ShouldBe(
            [new KeyValuePair<string, object?>(LogProperties.Repository, "octocat/hello-world")]);
    }

    [Fact]
    public void BeginOperationScope_Operation_BeginsScopeWithOperationProperty()
    {
        using var scope = _logger.BeginOperationScope("clone");

        _logger.States.ShouldHaveSingleItem().ShouldBe(
            [new KeyValuePair<string, object?>(LogProperties.Operation, "clone")]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void BeginRepositoryScope_EmptyRepository_Throws(string repository) =>
        Should.Throw<ArgumentException>(() => _logger.BeginRepositoryScope(repository));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void BeginOperationScope_EmptyOperation_Throws(string operation) =>
        Should.Throw<ArgumentException>(() => _logger.BeginOperationScope(operation));

    /// <summary>
    /// Records the state of every scope it begins and writes nothing.
    /// </summary>
    private sealed class ScopeRecordingLogger : ILogger
    {
        /// <summary>Gets the scope states, as name-value pairs, in the order the scopes began.</summary>
        public List<IEnumerable<KeyValuePair<string, object?>>> States { get; } = [];

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            States.Add(state.ShouldBeAssignableTo<IEnumerable<KeyValuePair<string, object?>>>()!);
            return null;
        }

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
