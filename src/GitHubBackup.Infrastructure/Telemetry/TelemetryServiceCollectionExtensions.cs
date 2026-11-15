using GitHubBackup.Core.Security;
using GitHubBackup.Core.Telemetry;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GitHubBackup.Infrastructure.Telemetry;

/// <summary>
/// Registers the OpenTelemetry SDK that exports the spans and metrics of <see cref="BackupTelemetry"/> and the log
/// entries over OTLP.
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    /// <summary>The <c>service.name</c> of the exported resource.</summary>
    public const string ServiceName = "GitHubBackup";

    /// <summary>The configuration key that switches the export on.</summary>
    private const string _enabledKey = OpenTelemetryOptions.SectionName + ":" + nameof(OpenTelemetryOptions.Enabled);

    /// <param name="services">The service collection of the host.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds the OpenTelemetry SDK with tracing and metrics for <see cref="BackupTelemetry.SourceName"/>, a logger
        /// provider with secrets masked, and an OTLP exporter for each, sending to
        /// <see cref="OpenTelemetryOptions.OtlpEndpoint"/>, when <see cref="OpenTelemetryOptions.Enabled"/> is
        /// <see langword="true"/>; otherwise adds nothing.
        /// </summary>
        /// <remarks>
        /// The logger provider receives the entries that Serilog lets through its levels; it requires
        /// <see cref="ISecretMasker"/> to be registered.
        /// The switch is read from <paramref name="configuration"/> because registration happens before the options
        /// exist. A value that is not a boolean registers nothing here and is reported by the validation of
        /// <see cref="OpenTelemetryOptions"/> when the host starts. The endpoint and the headers are read from the
        /// validated options when the exporter is created; <see cref="OpenTelemetryOptions.Headers"/> is the only
        /// source of headers. All three signals use the unnamed <see cref="OtlpExporterOptions"/>.
        /// </remarks>
        /// <param name="configuration">The configuration that holds the <c>OpenTelemetry</c> section.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public IServiceCollection AddGitHubBackupTelemetry(IConfiguration configuration)
        {
            if (!bool.TryParse(configuration[_enabledKey], out var enabled) || !enabled)
            {
                return services;
            }

            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService(ServiceName))
                .WithTracing(tracing => tracing.AddSource(BackupTelemetry.SourceName).AddOtlpExporter())
                .WithMetrics(metrics => metrics.AddMeter(BackupTelemetry.SourceName).AddOtlpExporter())
                .WithLogging(
                    logging => logging
                        .AddProcessor(provider => new SecretMaskingLogRecordProcessor(provider.GetRequiredService<ISecretMasker>()))
                        .AddOtlpExporter(),
                    ConfigureLogging);

            services.AddOptions<OtlpExporterOptions>()
                .Configure<IOptions<OpenTelemetryOptions>>((exporter, options) =>
                {
                    if (Uri.TryCreate(options.Value.OtlpEndpoint, UriKind.Absolute, out var endpoint))
                    {
                        exporter.Endpoint = endpoint;
                    }

                    // Assigned also when not set, so headers the SDK reads from OTEL_EXPORTER_OTLP_HEADERS never reach
                    // the receiver without being registered with the secret masker.
                    exporter.Headers = string.IsNullOrWhiteSpace(options.Value.Headers) ? null : options.Value.Headers;
                });

            return services;
        }
    }

    /// <summary>
    /// Sets what an exported log record carries: the rendered message and the properties of the entry as attributes.
    /// </summary>
    /// <remarks>
    /// Scopes stay out: the scope properties already reach the record as properties of the Serilog entry, and scope
    /// values would bypass <see cref="SecretMaskingLogRecordProcessor"/>.
    /// </remarks>
    /// <param name="options">The options of the OpenTelemetry logger provider.</param>
    private static void ConfigureLogging(OpenTelemetryLoggerOptions options)
    {
        options.IncludeFormattedMessage = true;
        options.IncludeScopes = false;
        options.ParseStateValues = true;
    }
}
