using System.Text;

namespace GitHubBackup.Infrastructure.IntegrationTests.Git;

/// <summary>
/// Tests that <see cref="GitHubBackup.Infrastructure.Git.GitClient"/> keeps the token out of the clone's git configuration and the log.
/// </summary>
public sealed class GitClientTokenTests : GitClientTestBase
{
    private const string _token = "ghp_0123456789abcdefghijklmnopqrstuvwxyzAB";

    [Fact]
    public async Task CloneAndUpdate_TokenSet_LeaveNoTokenInConfigsOrLog()
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("x-access-token:" + _token));
        var submodule = await _sandbox.CreateRemoteAsync("sub", CancellationToken);
        var top = await _sandbox.CreateRemoteAsync("top", CancellationToken);
        await top.AddSubmoduleAsync(submodule, "sub", CancellationToken);

        await CreateClient().CloneAsync(Request(branch: null, token: _token), top.Url, CancellationToken);
        await CreateClient().UpdateAsync(Request(branch: "missing", token: _token), CancellationToken);
        await CreateClient().GetRemoteRefsAsync(Request(token: _token), CancellationToken);
        await CreateClient().GetDefaultBranchAsync(Request(token: _token), CancellationToken);
        Records(_runnerLogger, _processStartingEventId).ShouldContain(record => record.GetStructuredStateValue("CommandLine")!.Contains("ls-remote"));

        var configs = Directory.EnumerateFiles(Path.Combine(_clonePath, ".git", "modules"), "config", SearchOption.AllDirectories)
            .Prepend(Path.Combine(_clonePath, ".git", "config"))
            .ToList();
        configs.Count.ShouldBe(2);
        foreach (var config in configs)
        {
            var content = File.ReadAllText(config);
            content.ShouldNotContain(_token);
            content.ShouldNotContain(encoded);
            content.ShouldNotContain("extraheader", Case.Insensitive);
        }

        var logged = _runnerLogger.Collector.GetSnapshot().Concat(_clientLogger.Collector.GetSnapshot())
            .SelectMany(record => (record.StructuredState ?? []).Select(pair => pair.Value ?? string.Empty).Prepend(record.Message))
            .ToList();
        logged.ShouldNotBeEmpty();
        logged.ShouldAllBe(text => !text.Contains(_token) && !text.Contains(encoded));
    }
}
