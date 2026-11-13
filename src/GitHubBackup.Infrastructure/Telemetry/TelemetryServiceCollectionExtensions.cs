using GitHubBackup.Core.Telemetry;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GitHubBackup.Infrastructure.Telemetry;

/// <summary>
/// Registers the OpenTelemetry SDK that exports the spans and metrics of <see cref="BackupTelemetry"/> over OTLP.
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
        /// Adds the OpenTelemetry SDK with tracing and metrics for <see cref="BackupTelemetry.SourceName"/> and an OTLP
        /// exporter for each, sending to <see cref="OpenTelemetryOptions.OtlpEndpoint"/>, when
        /// <see cref="OpenTelemetryOptions.Enabled"/> is <see langword="true"/>; otherwise adds nothing.
        /// </summary>
        /// <remarks>
        /// The switch is read from <paramref name="configuration"/> because registration happens before the options
        /// exist. A value that is not a boolean registers nothing here and is reported by the validation of
        /// <see cref="OpenTelemetryOptions"/> when the host starts. The endpoint is read from the validated options
        /// when the exporter is created.
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
                .WithMetrics(metrics => metrics.AddMeter(BackupTelemetry.SourceName).AddOtlpExporter());

            services.AddOptions<OtlpExporterOptions>()
                .Configure<IOptions<OpenTelemetryOptions>>((exporter, options) =>
                {
                    if (Uri.TryCreate(options.Value.OtlpEndpoint, UriKind.Absolute, out var endpoint))
                    {
                        exporter.Endpoint = endpoint;
                    }
                });

            return services;
        }
    }
}
