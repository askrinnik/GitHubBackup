using GitHubBackup.Core.Security;
using GitHubBackup.Infrastructure.Logging;
using GitHubBackup.TestUtilities;
using Microsoft.Extensions.Logging;

namespace GitHubBackup.Infrastructure.IntegrationTests.Logging;

/// <summary>
/// Tests <see cref="StartupLogging"/> against real files in a temporary content root.
/// </summary>
public sealed class StartupLoggingTests : IDisposable
{
    /// <summary>A secret without a recognisable format, masked only because it is registered.</summary>
    private const string _secret = "registered-secret-without-format";

    private readonly TemporaryContentRoot _contentRoot = new();

    [Fact]
    public void CreateLoggerFactory_Entry_WritesBothDailyFiles()
    {
        WriteEntry(new SecretMasker());

        File.Exists(_contentRoot.GetLogFilePath(LogFiles.TextExtension)).ShouldBeTrue();
        File.Exists(_contentRoot.GetLogFilePath(LogFiles.JsonExtension)).ShouldBeTrue();
    }

    [Fact]
    public void CreateLoggerFactory_SecretInMessageAndException_IsMaskedInBothFiles()
    {
        var masker = new SecretMasker();
        masker.Register(_secret);

        WriteEntry(masker);

        foreach (var content in new[] { _contentRoot.ReadTextLog(), File.ReadAllText(_contentRoot.GetLogFilePath(LogFiles.JsonExtension)) })
        {
            content.ShouldNotContain(_secret);
            content.ShouldContain("***");
        }
    }

    [Fact]
    public void CreateLoggerFactory_AfterDispose_FilesHoldTheWholeEntry()
    {
        WriteEntry(new SecretMasker());

        _contentRoot.ReadTextLog().ShouldContain("System.InvalidOperationException: Failed for");
        var entry = _contentRoot.ReadJsonLogEntries().ShouldHaveSingleItem();
        entry.GetProperty("@x").GetString().ShouldNotBeNull().ShouldStartWith("System.InvalidOperationException: Failed for");
    }

    /// <summary>
    /// Deletes the content root.
    /// </summary>
    public void Dispose() => _contentRoot.Dispose();

    /// <summary>
    /// Writes one error entry whose message and exception carry <see cref="_secret"/>, then disposes the factory.
    /// </summary>
    /// <param name="masker">The masker of the factory.</param>
    private void WriteEntry(ISecretMasker masker)
    {
        using var factory = StartupLogging.CreateLoggerFactory(_contentRoot.FolderPath, masker);
        TestLog.CallFailed(
            factory.CreateLogger<StartupLoggingTests>(),
            new InvalidOperationException($"Failed for {_secret}"),
            _secret,
            "other",
            "header");
    }
}
