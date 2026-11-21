using GitHubBackup.Core.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Validates <see cref="SerilogEndpointsOptions"/>; each failure starts with the full configuration key.
/// </summary>
/// <remarks>
/// The binder silently skips a value where a section is expected and a section without a matching property, so the
/// structure (each section is an object, each endpoint name is known) is checked on the raw
/// <c>Serilog:Endpoints</c> section of <paramref name="configuration"/>. The keys of every endpoint are checked in
/// both executables, because both read the same <c>appsettings.json</c>.
/// </remarks>
/// <param name="configuration">The application configuration.</param>
internal sealed class SerilogEndpointsOptionsValidator(IConfiguration configuration) : IValidateOptions<SerilogEndpointsOptions>
{
    /// <summary>The names of the endpoint sections, in the order failures are reported.</summary>
    private static readonly string[] _endpointNames = Enum.GetNames<LogEndpoint>();

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SerilogEndpointsOptions options)
    {
        List<string> failures = [];
        AddStructureFailures(failures);
        foreach (var endpoint in Enum.GetValues<LogEndpoint>())
        {
            AddLevelFailures(failures, endpoint, options.Get(endpoint));
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Adds a failure for each value that stands where a section is expected and for each unknown endpoint name.
    /// </summary>
    /// <param name="failures">The list to add to.</param>
    private void AddStructureFailures(List<string> failures)
    {
        var section = configuration.GetSection(SerilogEndpointsOptions.SectionName);
        if (HasValue(section))
        {
            failures.Add(NotAnObject(SerilogEndpointsOptions.SectionName));
            return;
        }

        foreach (var endpoint in section.GetChildren().OrderBy(child => child.Key, StringComparer.Ordinal))
        {
            if (!_endpointNames.Contains(endpoint.Key, StringComparer.OrdinalIgnoreCase))
            {
                failures.Add(
                    $"{endpoint.Path} is not a log endpoint; the endpoints are {string.Join(", ", _endpointNames)}.");
            }
            else if (HasValue(endpoint))
            {
                failures.Add(NotAnObject(endpoint.Path));
            }
            else if (endpoint.GetSection(nameof(SerilogEndpointOptions.Override)) is var overrides && HasValue(overrides))
            {
                failures.Add(NotAnObject(overrides.Path));
            }
        }
    }

    /// <summary>
    /// Adds a failure for an unknown level and for each unknown level of an override of <paramref name="endpoint"/>.
    /// </summary>
    /// <param name="failures">The list to add to.</param>
    /// <param name="endpoint">The endpoint.</param>
    /// <param name="options">The bound options of the endpoint.</param>
    private static void AddLevelFailures(List<string> failures, LogEndpoint endpoint, SerilogEndpointOptions options)
    {
        var prefix = $"{SerilogEndpointsOptions.SectionName}:{endpoint}";
        if (!string.IsNullOrEmpty(options.Level) && !LogLevels.IsEndpointName(options.Level))
        {
            failures.Add(UnknownLevel($"{prefix}:{nameof(SerilogEndpointOptions.Level)}"));
        }

        foreach (var (source, level) in options.Override.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!LogLevels.IsEndpointName(level))
            {
                failures.Add(UnknownLevel($"{prefix}:{nameof(SerilogEndpointOptions.Override)}:{source}"));
            }
        }
    }

    /// <summary>
    /// Returns whether <paramref name="section"/> holds a value; an empty value is what a JSON <c>null</c> binds to.
    /// </summary>
    /// <param name="section">The section.</param>
    /// <returns><see langword="true"/> when the section holds a non-empty value.</returns>
    private static bool HasValue(IConfigurationSection section) => !string.IsNullOrEmpty(section.Value);

    /// <summary>
    /// Creates the failure message for a value at <paramref name="key"/>, where an object is expected.
    /// </summary>
    /// <param name="key">The full configuration key.</param>
    /// <returns>The message.</returns>
    private static string NotAnObject(string key) => $"{key} must be an object, not a value.";

    /// <summary>
    /// Creates the failure message for an unknown level at <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The full configuration key.</param>
    /// <returns>The message.</returns>
    private static string UnknownLevel(string key) =>
        $"{key} must be one of {string.Join(", ", LogLevels.EndpointNames)}.";
}
