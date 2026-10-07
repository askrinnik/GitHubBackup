using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Core.Configuration;

/// <summary>
/// Binds option classes to configuration sections so that binding errors surface as validation failures.
/// </summary>
public static class SectionBindingExtensions
{
    /// <param name="builder">The options builder to configure.</param>
    /// <typeparam name="TOptions">The option class bound to the section.</typeparam>
    extension<TOptions>(OptionsBuilder<TOptions> builder)
        where TOptions : class
    {
        /// <summary>
        /// Binds the options to the configuration section <paramref name="sectionName"/>.
        /// </summary>
        /// <remarks>
        /// A value that cannot be converted to the property type (<c>Backup:ShortHashLength=abc</c>) is reported as an
        /// <see cref="OptionsValidationException"/>, so start-up validation collects it together with the rule
        /// violations of the other sections instead of failing on the first one. The message names the key and the
        /// target type and never the value, which may be a secret.
        /// </remarks>
        /// <param name="configuration">The configuration that holds the section.</param>
        /// <param name="sectionName">The name of the section, for example <c>Backup</c>.</param>
        /// <returns>The same builder, for chaining.</returns>
        public OptionsBuilder<TOptions> BindSection(IConfiguration configuration, string sectionName)
        {
            var optionsName = builder.Name;
            return builder.Configure(options =>
            {
                try
                {
                    configuration.GetSection(sectionName).Bind(options);
                }
                catch (InvalidOperationException exception)
                {
                    throw new OptionsValidationException(optionsName, typeof(TOptions), [DescribeBindingFailure<TOptions>(configuration.GetSection(sectionName), exception)]);
                }
            });
        }
    }

    /// <summary>
    /// Describes a binding failure without quoting the configuration value.
    /// </summary>
    /// <remarks>
    /// The binder reports <c>Failed to convert configuration value '&lt;value&gt;' at '&lt;key&gt;' to type '&lt;type&gt;'.</c>
    /// A key-value pair of the section is accepted only when its complete marker follows the fixed prefix and the rest
    /// of the message is a type name that ends the message, so a value that imitates the template cannot name a
    /// different key or inject text into the type. When no pair, or more than one, qualifies, the message names the
    /// section and the options type and the text of <paramref name="exception"/> is not used.
    /// </remarks>
    /// <typeparam name="TOptions">The option class bound to the section.</typeparam>
    /// <param name="section">The section that failed to bind.</param>
    /// <param name="exception">The exception thrown by the binder.</param>
    /// <returns>A message that holds the key and the target type, or the section name and the options type.</returns>
    internal static string DescribeBindingFailure<TOptions>(IConfigurationSection section, InvalidOperationException exception)
    {
        const string prefix = "Failed to convert configuration value ";
        var message = exception.Message;
        string? matchedPath = null;
        string? matchedType = null;
        var matches = 0;
        foreach (var (path, value) in section.AsEnumerable(false))
        {
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            var marker = $"{prefix}'{value}' at '{path}' to type '";
            if (!message.StartsWith(marker, StringComparison.Ordinal) || !message.EndsWith("'.", StringComparison.Ordinal))
            {
                continue;
            }

            var type = message[marker.Length..^2];
            if (type.Length > 0 && !type.Contains('\''))
            {
                matchedPath = path;
                matchedType = type;
                matches++;
            }
        }

        return matches == 1
            ? $"{matchedPath} cannot be converted to {matchedType}."
            : $"The {section.Key} section cannot be bound to {typeof(TOptions).Name}.";
    }
}
