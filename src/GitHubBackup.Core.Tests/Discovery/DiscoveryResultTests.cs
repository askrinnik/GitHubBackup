using GitHubBackup.Core.BackupConfiguration;
using GitHubBackup.Core.Discovery;
using GitHubBackup.Core.GitHub;

namespace GitHubBackup.Core.Tests.Discovery;

/// <summary>
/// Tests <see cref="DiscoveryResult"/>.
/// </summary>
public sealed class DiscoveryResultTests
{
    private static readonly GitHubRepository _repository =
        new(1, "octo", "tool", false, false, false, true, "main", "https://github.com/octo/tool");

    private static readonly TrackedRepository _tracked = new("octo", "tool", 1, RepositoryStatus.Active, "octo");

    [Fact]
    public void Empty_HasNoChanges() => DiscoveryResult.Empty.HasChanges.ShouldBeFalse();

    [Fact]
    public void HasChanges_NewInAccounts_IsTrue() =>
        (DiscoveryResult.Empty with { NewInAccounts = [new("octo", _repository)] }).HasChanges.ShouldBeTrue();

    [Fact]
    public void HasChanges_NewStandalone_IsTrue() =>
        (DiscoveryResult.Empty with { NewStandalone = [_repository] }).HasChanges.ShouldBeTrue();

    [Fact]
    public void HasChanges_Missing_IsTrue() =>
        (DiscoveryResult.Empty with { Missing = [_tracked] }).HasChanges.ShouldBeTrue();

    [Fact]
    public void HasChanges_Returned_IsTrue() =>
        (DiscoveryResult.Empty with { Returned = [new(_tracked, _repository)] }).HasChanges.ShouldBeTrue();

    [Fact]
    public void HasChanges_Renamed_IsTrue() =>
        (DiscoveryResult.Empty with { Renamed = [new(_tracked, _repository)] }).HasChanges.ShouldBeTrue();
}
