using GitHubBackup.Core.Logging;
using GitHubBackup.Core.Runs;
using Serilog.Core;
using Serilog.Events;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Adds the <see cref="LogProperties.RunId"/> of the current run to every log entry that does not carry one from a
/// scope.
/// </summary>
/// <remarks>
/// The identifier is read for each entry, because a host that performs several runs starts each of them with
/// <see cref="IRunContext.BeginRun"/>.
/// </remarks>
/// <param name="runContext">The run whose identifier is added.</param>
internal sealed class RunIdEnricher(IRunContext runContext) : ILogEventEnricher
{
    /// <inheritdoc />
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) =>
        logEvent.AddPropertyIfAbsent(
            new(LogProperties.RunId, new ScalarValue(runContext.RunId.ToString("D"))));
}
