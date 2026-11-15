using GitHubBackup.Core.Runs;
using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

namespace GitHubBackup.Infrastructure.Logging;

/// <summary>
/// Registers Serilog as the provider behind <c>ILogger&lt;T&gt;</c>, writing the daily files named by
/// <see cref="LogFiles"/>.
/// </summary>
public static class FileLoggingServiceCollectionExtensions
{
    /// <param name="services">The service collection of the host.</param>
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Adds Serilog with the levels of <see cref="SerilogOptions"/>, the run identifier in every entry and
        /// secrets masked in both files and in the <see cref="ILiveLogFeed"/>, which shows the same levels as the files.
        /// </summary>
        /// <remarks>
        /// The logger is created when the host is built, so an invalid level fails <c>Build()</c> with an
        /// <see cref="OptionsValidationException"/>. The logger belongs to the service provider and is disposed, with
        /// the files flushed and closed, when the host is disposed; the static <see cref="Log.Logger"/> is left
        /// untouched. Every <see cref="Microsoft.Extensions.Logging.ILoggerProvider"/> registered in the container
        /// receives the entries that pass the levels of <see cref="SerilogOptions"/>, with the run identifier and the
        /// scope properties; Serilog does not mask them, so such a provider masks secrets itself. Requires
        /// <see cref="IHostEnvironment"/>, <see cref="IRunContext"/> and <see cref="ISecretMasker"/> to be registered.
        /// </remarks>
        /// <param name="configuration">The application configuration.</param>
        /// <returns>The same service collection, for chaining.</returns>
        public IServiceCollection AddGitHubBackupLogging(IConfiguration configuration)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SerilogOptions>, SerilogOptionsValidator>());
            services.AddOptions<SerilogOptions>().BindSection(configuration, SerilogOptions.SectionName).ValidateOnStart();
            services.TryAddSingleton<LiveLogSink>();
            services.TryAddSingleton<ILiveLogFeed>(provider => provider.GetRequiredService<LiveLogSink>());
            services.AddSerilog(ConfigureLogger, preserveStaticLogger: true, writeToProviders: true);

            return services;
        }
    }

    /// <summary>
    /// Configures the levels, the run identifier and the sinks of the logger: the two files and the live log feed.
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

        WriteToDailyFiles(configuration, contentRootPath, masker);
        configuration.WriteTo.Sink(provider.GetRequiredService<LiveLogSink>());
    }

    /// <summary>
    /// Adds the sinks of the text and the structured daily files in the log folder of the content root, both masked.
    /// </summary>
    /// <param name="configuration">The logger configuration.</param>
    /// <param name="contentRootPath">The folder of the executable.</param>
    /// <param name="masker">The masker applied to every rendered entry.</param>
    internal static void WriteToDailyFiles(
        LoggerConfiguration configuration, string contentRootPath, ISecretMasker masker)
    {
        WriteToDailyFile(
            configuration,
            new TextLogFormatter(),
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
