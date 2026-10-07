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
        /// violations of the other sections instead of failing on the first one.
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
                    throw new OptionsValidationException(optionsName, typeof(TOptions), [exception.Message]);
                }
            });
        }
    }
}
