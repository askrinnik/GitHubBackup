using System.IO.Abstractions;
using GitHubBackup.Infrastructure.Files;
using GitHubBackup.TestUtilities;

namespace GitHubBackup.Infrastructure.IntegrationTests.Files;

/// <summary>
/// Moves small files in a temporary folder to the real Recycle Bin of the system volume through
/// <see cref="ShellRecycleBin"/>.
/// </summary>
/// <remarks>
/// A successful test leaves one tiny file whose name starts with <see cref="_prefix"/> in the Recycle Bin of the user.
/// </remarks>
public sealed class ShellRecycleBinTests : IDisposable
{
    private const string _prefix = "GitHubBackup-test-";

    private readonly string _folder = TemporaryFolder.Create();

    private readonly ShellRecycleBin _recycleBin = new(new FileSystem());

    [Fact]
    public async Task RecycleAsync_ExistingFile_MovesItAway()
    {
        var path = CreateFile();

        var outcome = await _recycleBin.RecycleAsync(path, TestContext.Current.CancellationToken);

        outcome.ShouldBe(GitHubBackup.Core.Files.RecycleOutcome.Success);
        File.Exists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task RecycleAsync_LockedFile_FailsAndKeepsFile()
    {
        var path = CreateFile();
        string? error;
        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var outcome = await _recycleBin.RecycleAsync(path, TestContext.Current.CancellationToken);

            outcome.Succeeded.ShouldBeFalse();
            error = outcome.Error;
        }

        error.ShouldNotBeNullOrWhiteSpace();
        File.Exists(path).ShouldBeTrue();
    }

    [Fact]
    public async Task RecycleAsync_MissingFile_Fails()
    {
        var path = Path.Combine(_folder, $"{_prefix}missing.zip");

        var outcome = await _recycleBin.RecycleAsync(path, TestContext.Current.CancellationToken);

        outcome.Succeeded.ShouldBeFalse();
        outcome.Error.ShouldNotBeNull().ShouldContain(path);
    }

    [Fact]
    public async Task RecycleAsync_Cancelled_ThrowsAndKeepsFile()
    {
        var path = CreateFile();

        await Should.ThrowAsync<OperationCanceledException>(
            () => _recycleBin.RecycleAsync(path, new CancellationToken(canceled: true)));

        File.Exists(path).ShouldBeTrue();
    }

    /// <summary>
    /// Deletes the temporary folder.
    /// </summary>
    public void Dispose() => TemporaryFolder.Delete(_folder);

    /// <summary>
    /// Creates a tiny file with a recognisable name in the temporary folder.
    /// </summary>
    /// <returns>The absolute path of the file.</returns>
    private string CreateFile()
    {
        var path = Path.Combine(_folder, $"{_prefix}{Guid.NewGuid():N}.zip");
        File.WriteAllBytes(path, [1, 2, 3]);
        return path;
    }
}
