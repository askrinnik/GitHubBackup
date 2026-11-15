namespace GitHubBackup.Infrastructure.Configuration;

/// <summary>
/// Decides whether an address may receive a secret: it must be encrypted, unless it never leaves the machine.
/// </summary>
internal static class SecureEndpoint
{
    /// <summary>
    /// Returns whether <paramref name="address"/> is an absolute <c>https</c> URI, or an <c>http</c> URI of a
    /// loopback host.
    /// </summary>
    /// <param name="address">The configured address; <see langword="null"/> is never allowed.</param>
    /// <returns><see langword="true"/> when a secret may be sent to the address.</returns>
    public static bool IsAllowed(string? address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));
}
