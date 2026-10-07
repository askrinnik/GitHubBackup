using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace GitHubBackup.Cli.Tests;

/// <summary>
/// Serves files from memory so that configuration tests do not touch the disk.
/// </summary>
/// <param name="files">The file contents by file name.</param>
internal sealed class InMemoryFileProvider(IReadOnlyDictionary<string, string> files) : IFileProvider
{
    /// <summary>
    /// Creates a provider that serves <paramref name="content"/> as <c>appsettings.json</c>.
    /// </summary>
    /// <param name="content">The JSON text of the settings file.</param>
    /// <returns>The file provider.</returns>
    public static InMemoryFileProvider WithAppSettings(string content) =>
        new(new Dictionary<string, string> { ["appsettings.json"] = content });

    /// <inheritdoc />
    public IFileInfo GetFileInfo(string subpath) =>
        files.TryGetValue(subpath, out var content) ? new InMemoryFileInfo(subpath, content) : new NotFoundFileInfo(subpath);

    /// <inheritdoc />
    public IDirectoryContents GetDirectoryContents(string subpath) => NotFoundDirectoryContents.Singleton;

    /// <inheritdoc />
    public IChangeToken Watch(string filter) => NullChangeToken.Singleton;

    /// <summary>
    /// Describes one in-memory file; it has no physical path, so readers use <see cref="CreateReadStream"/>.
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <param name="content">The text content, encoded as UTF-8.</param>
    private sealed class InMemoryFileInfo(string name, string content) : IFileInfo
    {
        /// <summary>The UTF-8 bytes of the content.</summary>
        private readonly byte[] _bytes = Encoding.UTF8.GetBytes(content);

        /// <inheritdoc />
        public bool Exists => true;

        /// <inheritdoc />
        public long Length => _bytes.Length;

        /// <inheritdoc />
        public string? PhysicalPath => null;

        /// <inheritdoc />
        public string Name => name;

        /// <inheritdoc />
        public DateTimeOffset LastModified => DateTimeOffset.UnixEpoch;

        /// <inheritdoc />
        public bool IsDirectory => false;

        /// <inheritdoc />
        public Stream CreateReadStream() => new MemoryStream(_bytes, writable: false);
    }
}
