using System.IO.Abstractions;
using System.Text.Json;
using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Configuration;
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
/// <param name="logger">The logger.</param>
internal sealed class JsonBackupConfigStore(
    IFileSystem fileSystem,
    IOptions<BackupOptions> options,
    IHostEnvironment environment,
    IBackupConfigValidator validator,
    ILogger<JsonBackupConfigStore> logger) : IBackupConfigStore
{
    /// <summary>The extension appended to the configuration path for the copy of the previous file.</summary>
    public const string BackupExtension = ".bak";

    /// <summary>The extension of the temporary file written before it replaces the configuration file.</summary>
    public const string TemporaryExtension = ".tmp";

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
    /// <remarks>
    /// The new content goes to a uniquely named temporary file in the folder of the configuration file and is flushed
    /// to disk; only then does it replace the file, so a crash at any point leaves either the old or the new file
    /// under the final name, never a partial one.
    /// </remarks>
    public async Task SaveAsync(BackupConfig config, CancellationToken cancellationToken)
    {
        var path = ConfigPath;
        var result = validator.Validate(config, DefaultSourcesRoot);
        if (!result.IsValid)
        {
            throw new BackupConfigException(path, result.Errors);
        }

        var content = JsonSerializer.SerializeToUtf8Bytes(config, BackupConfigJsonContext.BackupConfigTypeInfo);
        var temporaryPath = $"{path}.{Guid.NewGuid():N}{TemporaryExtension}";
        var backupPath = path + BackupExtension;
        var replacing = false;
        try
        {
            await using (var stream = fileSystem.FileStream.New(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await stream.WriteAsync(content, cancellationToken);
                await stream.WriteAsync(_finalNewLine, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (fileSystem.File.Exists(path))
            {
                replacing = true;
                fileSystem.File.Replace(temporaryPath, path, backupPath);
            }
            else
            {
                fileSystem.File.Move(temporaryPath, path);
            }
        }
        catch
        {
            if (replacing)
            {
                RestoreFinalFile(path, temporaryPath, backupPath);
            }

            // After a failed replace the complete temporary file may be the only copy of the new content, so it is
            // kept unless the final name holds a file again.
            if (!replacing || fileSystem.File.Exists(path))
            {
                TryDelete(temporaryPath);
            }

            throw;
        }

        BackupConfigLog.Saved(logger, path);
    }

    /// <summary>
    /// Puts a file back under the final name when a failed replace has already moved the original to the backup name.
    /// </summary>
    /// <remarks>
    /// <see cref="IFile.Replace(string, string, string?)"/> can fail after renaming the original to the backup name
    /// and before moving the replacement in, which leaves no file under the final name. The complete temporary file
    /// is moved in first; if that fails too, the original is copied back from the backup. Failures of these attempts
    /// are ignored: the exception of the replace is the one the caller needs.
    /// </remarks>
    /// <param name="path">The path of the configuration file.</param>
    /// <param name="temporaryPath">The path of the fully written temporary file.</param>
    /// <param name="backupPath">The path of the copy of the previous file.</param>
    private void RestoreFinalFile(string path, string temporaryPath, string backupPath)
    {
        if (fileSystem.File.Exists(path) || !fileSystem.File.Exists(backupPath))
        {
            return;
        }

        try
        {
            if (fileSystem.File.Exists(temporaryPath))
            {
                fileSystem.File.Move(temporaryPath, path);
                return;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Falls through to restoring the previous file.
        }

        try
        {
            fileSystem.File.Copy(backupPath, path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done; the previous content stays in the backup file.
        }
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

    /// <summary>
    /// Deletes a temporary file left by a failed write, ignoring a failure to delete it.
    /// </summary>
    /// <remarks>
    /// The write has already failed and its exception is the one the caller needs; a leftover temporary file is
    /// harmless because the next write uses a new name.
    /// </remarks>
    /// <param name="temporaryPath">The path of the temporary file.</param>
    private void TryDelete(string temporaryPath)
    {
        try
        {
            fileSystem.File.Delete(temporaryPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Best effort only: the exception of the failed write is rethrown by the caller.
        }
    }
}
