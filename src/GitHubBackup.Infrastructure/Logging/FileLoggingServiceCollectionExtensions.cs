using System.Globalization;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Formatting;
using Serilog.Formatting.Compact;
using Serilog.Formatting.Display;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Registers Serilog as the provider behind <c>ILogger&lt;T&gt;</c>, writing the daily files named by
/// <see cref="LogFiles"/>.
/// </summary>
public static class FileLoggingServiceCollectionExtensions
{
    /// <summary>The layout of one entry in the text log.</summary>
    /// <remarks>
    /// <c>{Properties:j}</c> renders the properties that neither the message nor the layout shows, such as
    /// <c>Repository</c> and <c>Operation</c> from a scope.
    /// </remarks>
    internal const string TextOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {RunId} {SourceContext}: {Message:lj} {Properties:j}{NewLine}{Exception}";

    /// <param name="services">The service collection of the host.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds Serilog with the levels of <see cref="SerilogOptions"/>, the run identifier in every entry and
        /// secrets masked in both files.
        /// </summary>
        /// <remarks>
        /// The logger is created when the host is built, so an invalid level fails <c>Build()</c> with an
        /// <see cref="OptionsValidationException"/>. The logger belongs to the service provider and is disposed, with
        /// the files flushed and closed, when the host is disposed; the static <see cref="Log.Logger"/> is left
        /// untouched. Requires <see cref="IHostEnvironment"/>, <see cref="IRunContext"/> and
        /// <see cref="ISecretMasker"/> to be registered.
        /// </remarks>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public IServiceCollection AddGitHubBackupLogging(IConfiguration configuration)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SerilogOptions>, SerilogOptionsValidator>());
            services.AddOptions<SerilogOptions>().BindSection(configuration, SerilogOptions.SectionName).ValidateOnStart();
            services.AddSerilog(ConfigureLogger, preserveStaticLogger: true);

            return services;
        }
    }

    /// <summary>
    /// Configures the levels, the run identifier and the two file sinks of the logger.
    /// </summary>
    /// <param name="provider">The service provider of the host.</param>
    /// <param name="configuration">The logger configuration to fill.</param>
    private static void ConfigureLogger(IServiceProvider provider, LoggerConfiguration configuration)
    {
        var options = provider.GetRequiredService<IOptions<SerilogOptions>>().Value;
        var contentRootPath = provider.GetRequiredService<IHostEnvironment>().ContentRootPath;
        var masker = provider.GetRequiredService<ISecretMasker>();

        configuration.MinimumLevel.Is(SerilogOptionsValidator.ParseLevel(options.Default));
        foreach (var (source, level) in options.Override)
        {
            configuration.MinimumLevel.Override(source, SerilogOptionsValidator.ParseLevel(level));
        }

        configuration.Enrich.With(new RunIdEnricher(provider.GetRequiredService<IRunContext>()));

        WriteToDailyFile(
            configuration,
            new MessageTemplateTextFormatter(TextOutputTemplate, CultureInfo.InvariantCulture),
            masker,
            LogFiles.GetPathTemplate(contentRootPath, LogFiles.TextExtension));
        WriteToDailyFile(
            configuration,
            new CompactJsonFormatter(),
            masker,
            LogFiles.GetPathTemplate(contentRootPath, LogFiles.JsonExtension));
    }

    /// <summary>
    /// Adds a sink that writes masked entries to one file per local day and never deletes old files.
    /// </summary>
    /// <remarks>
    /// The file is shared so that the console and the WPF executables can write to the same day's file at once.
    /// </remarks>
    /// <param name="configuration">The logger configuration.</param>
    /// <param name="formatter">The formatter that renders an entry.</param>
    /// <param name="masker">The masker applied to the rendered entry.</param>
    /// <param name="pathTemplate">The path returned by <see cref="LogFiles.GetPathTemplate"/>.</param>
    private static void WriteToDailyFile(
        LoggerConfiguration configuration, ITextFormatter formatter, ISecretMasker masker, string pathTemplate) =>
        configuration.WriteTo.File(
            new MaskingTextFormatter(formatter, masker),
            pathTemplate,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: null,
            shared: true);
}
