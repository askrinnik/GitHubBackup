using System.IO.Abstractions;
using System.Text.Json;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Configuration;
using GitHubBackup.Infrastructure.Files;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GitHubBackup.Infrastructure.BackupConfiguration;

/// <summary>
/// Reads and writes <c>backup-config.json</c> at <see cref="BackupOptions.ConfigPath"/> with
/// <see cref="BackupConfigJsonContext"/>.
/// </summary>
/// <param name="fileSystem">The file system.</param>
/// <param name="options">The options that name the configuration file.</param>
/// <param name="environment">The host environment whose content root holds the default clone folder.</param>
/// <param name="validator">The validator applied on every read and write.</param>
/// <param name="writer">The writer that replaces the configuration file atomically.</param>
/// <param name="logger">The logger.</param>
internal sealed class JsonBackupConfigStore(
    IFileSystem fileSystem,
    IOptions<BackupOptions> options,
    IHostEnvironment environment,
    IBackupConfigValidator validator,
    AtomicFileWriter writer,
    ILogger<JsonBackupConfigStore> logger) : IBackupConfigStore
{
    /// <summary>The line break that ends the file, matching the line breaks inside it.</summary>
    private static readonly byte[] _finalNewLine = "\r\n"u8.ToArray();

    /// <summary>Gets the absolute path of the configuration file.</summary>
    private string ConfigPath => options.Value.ConfigPath;

    /// <summary>Gets the clone folder used when the configuration does not set one.</summary>
    private string DefaultSourcesRoot => Path.Combine(environment.ContentRootPath, BackupConfig.DefaultSourcesFolderName);

    /// <inheritdoc />
    public async Task<BackupConfigLoadResult> LoadAsync(CancellationToken cancellationToken)
    {
        var path = ConfigPath;
        var config = await ReadAsync(path, cancellationToken);

        var result = validator.Validate(config, DefaultSourcesRoot);
        if (!result.IsValid)
        {
            throw new BackupConfigException(path, result.Errors);
        }

        foreach (var warning in result.Warnings)
        {
            BackupConfigLog.ValidationWarning(logger, path, warning);
        }

        BackupConfigLog.Loaded(logger, path, config.Accounts.Count, config.Repositories.Count);
        return new(config, result.Warnings);
    }

    /// <inheritdoc />
    public BackupConfigValidationResult Validate(BackupConfig config) => validator.Validate(config, DefaultSourcesRoot);

    /// <inheritdoc />
    /// <remarks>
    /// The file is replaced through <see cref="AtomicFileWriter"/>, which keeps the previous version as a backup.
    /// </remarks>
    public async Task SaveAsync(BackupConfig config, CancellationToken cancellationToken)
    {
        var path = ConfigPath;
        var result = Validate(config);
        if (!result.IsValid)
        {
            throw new BackupConfigException(path, result.Errors);
        }

        var content = JsonSerializer.SerializeToUtf8Bytes(config, BackupConfigJsonContext.BackupConfigTypeInfo);
        await writer.WriteAsync(path, content.Concat(_finalNewLine).ToArray(), cancellationToken);

        BackupConfigLog.Saved(logger, path);
    }

    /// <summary>
    /// Builds the error that describes a JSON problem by its position only.
    /// </summary>
    /// <remarks>
    /// The message of a <see cref="JsonException"/> can quote part of the file, and a pasted value can carry a token,
    /// so only the line, the position and the JSON path are reported.
    /// </remarks>
    /// <param name="path">The path of the configuration file.</param>
    /// <param name="exception">The parse or conversion failure.</param>
    /// <returns>The error message.</returns>
    private static string DescribeJsonError(string path, JsonException exception) =>
        $"The backup configuration file '{path}' is not valid at line {exception.LineNumber + 1 ?? 1}, "
        + $"position {exception.BytePositionInLine + 1 ?? 1} (JSON path {exception.Path ?? "$"}): "
        + "check the syntax, the property names, the value types and that required values are not null.";

    /// <summary>
    /// Reads and deserializes the configuration file.
    /// </summary>
    /// <param name="path">The path of the configuration file.</param>
    /// <param name="cancellationToken">Cancels reading the file.</param>
    /// <returns>The deserialized configuration, not yet validated.</returns>
    /// <exception cref="BackupConfigException">The file is missing, unreadable or not a valid configuration document.</exception>
    private async Task<BackupConfig> ReadAsync(string path, CancellationToken cancellationToken)
    {
        BackupConfig? config;
        try
        {
            await using var stream = fileSystem.File.OpenRead(path);
            config = await JsonSerializer.DeserializeAsync(stream, BackupConfigJsonContext.BackupConfigTypeInfo, cancellationToken);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new BackupConfigException(path, [$"The backup configuration file '{path}' does not exist."], exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new BackupConfigException(path, [$"The backup configuration file '{path}' cannot be read: {exception.Message}"], exception);
        }
        catch (JsonException exception)
        {
            // The JsonException is not attached as the inner exception: its message can quote part of the file.
            throw new BackupConfigException(path, [DescribeJsonError(path, exception)]);
        }

        return config
            ?? throw new BackupConfigException(path, [$"The backup configuration file '{path}' must contain a JSON object, not null."]);
    }
}
