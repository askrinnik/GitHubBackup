using System.Globalization;

namespace GitHubBackup.TestUtilities;

/// <summary>
/// Holds the named scenarios of the desktop app that the screenshot tests and the test bench start; none of them
/// needs git, so a dry run and the discovery dialog show their rows without touching a remote.
/// </summary>
public static class UiScenarios
{
    /// <summary>The owner of the repositories the scenarios track.</summary>
    public const string Owner = "octo";

    /// <summary>The owner of the repositories that discovery offers as new.</summary>
    public const string NewOwner = "newcomer";

    /// <summary>The <c>owner/name</c> of the tracked repository that the API reports as gone.</summary>
    public const string MissingRepositoryName = Owner + "/gone-repository";

    /// <summary>The number of tracked repositories that the API reports as gone in <see cref="ManyRows"/>.</summary>
    public const int ManyRowsCount = 12;

    /// <summary>The id of the repository that the API reports as gone.</summary>
    private const long _missingId = 101;

    /// <summary>The id of the repository that the API reports under another name.</summary>
    private const long _renamedId = 102;

    /// <summary>The id of the unavailable repository that the API reports again.</summary>
    private const long _returnedId = 103;

    /// <summary>The first id of the repositories that discovery offers as new.</summary>
    private const long _firstNewId = 201;

    /// <summary>The first id of the gone repositories of <see cref="ManyRows"/>.</summary>
    private const long _firstManyRowsId = 301;

    /// <summary>
    /// Gets the scenario with one tracked repository that the API answers with 404: a single-repository run asks to
    /// mark it unavailable, a dry run reports it in the result table.
    /// </summary>
    public static UiScenario MissingRepository { get; } = new(
        "missing-repository",
        $"{MissingRepositoryName} is tracked but GitHub answers 404. Select it and run Backup repository, or run a dry run.",
        (api, config) => AddMissing(api, config, "gone-repository", _missingId));

    /// <summary>Gets the scenario with one tracked repository that the API reports under another name.</summary>
    public static UiScenario RenamedRepository { get; } = new(
        "renamed-repository",
        $"{Owner}/old-name is tracked; GitHub reports the same id as {Owner}/new-name. Run Backup all to review the rename.",
        (api, config) => AddRenamed(api, config));

    /// <summary>Gets the scenario where the token can access repositories that the configuration does not track.</summary>
    public static UiScenario NewRepositories { get; } = new(
        "new-repositories",
        $"The token can access three repositories of {NewOwner} that are not tracked. Run Backup all to review them.",
        (api, config) => AddNew(api, config, 3));

    /// <summary>
    /// Gets the scenario whose discovery dialog shows one row of each kind: New, Renamed, Missing and Returned.
    /// </summary>
    public static UiScenario DiscoveryReview { get; } = new(
        "discovery-review",
        "Run Backup all: the discovery dialog shows a New, a Renamed, a Missing and a Returned row.",
        (api, config) =>
        {
            var listed = new List<string>();
            listed.AddRange(NewRepositoryJson(api, 1));
            listed.Add(api.GivenRepository(_renamedId, Owner, "new-name"));
            listed.Add(api.GivenRepository(_returnedId, Owner, "back-again"));
            api.GivenCurrentUserRepositories([.. listed]);
            config.WithDiscoverAccessibleRepositories()
                .WithRepository(Owner, "old-name", _renamedId)
                .WithRepository(Owner, "back-again", _returnedId, "Unavailable");
            AddMissing(api, config, "gone-repository", _missingId);
        });

    /// <summary>
    /// Gets the scenario with many tracked repositories that the API reports as gone and three new ones: a dry run
    /// fills the result table and lists the planned discovery changes.
    /// </summary>
    public static UiScenario ManyRows { get; } = new(
        "many-rows",
        $"{ManyRowsCount} tracked repositories are gone and three new ones are accessible. Turn on Dry run and run Backup all.",
        (api, config) =>
        {
            AddNew(api, config, 3);
            for (var index = 0; index < ManyRowsCount; index++)
            {
                var name = string.Create(CultureInfo.InvariantCulture, $"gone-repository-{index + 1:00}");
                AddMissing(api, config, name, _firstManyRowsId + index);
            }
        });

    /// <summary>Gets every scenario, in the order the test bench lists them.</summary>
    public static IReadOnlyList<UiScenario> All { get; } =
        [MissingRepository, RenamedRepository, NewRepositories, DiscoveryReview, ManyRows];

    /// <summary>
    /// Returns the scenario with <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The name of the scenario, compared without regard to case.</param>
    /// <returns>The scenario, or <see langword="null"/> when no scenario has that name.</returns>
    public static UiScenario? Find(string name) =>
        All.FirstOrDefault(scenario => string.Equals(scenario.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Tracks a repository of <see cref="Owner"/> and makes the API answer its lookups with 404.
    /// </summary>
    /// <param name="api">The API stub.</param>
    /// <param name="config">The configuration builder.</param>
    /// <param name="name">The repository name.</param>
    /// <param name="id">The repository id.</param>
    private static void AddMissing(GitHubApiStub api, BackupConfigJson config, string name, long id)
    {
        api.GivenRepositoryUnavailable(id, Owner, name);
        config.WithRepository(Owner, name, id);
    }

    /// <summary>
    /// Tracks <c>octo/old-name</c> and makes the API report its id as <c>octo/new-name</c>.
    /// </summary>
    /// <param name="api">The API stub.</param>
    /// <param name="config">The configuration builder.</param>
    private static void AddRenamed(GitHubApiStub api, BackupConfigJson config)
    {
        api.GivenCurrentUserRepositories(api.GivenRepository(_renamedId, Owner, "new-name"));
        config.WithRepository(Owner, "old-name", _renamedId);
    }

    /// <summary>
    /// Lists <paramref name="count"/> untracked repositories of <see cref="NewOwner"/> as accessible to the token and
    /// lets discovery offer repositories of other owners.
    /// </summary>
    /// <param name="api">The API stub.</param>
    /// <param name="config">The configuration builder.</param>
    /// <param name="count">The number of new repositories.</param>
    private static void AddNew(GitHubApiStub api, BackupConfigJson config, int count)
    {
        api.GivenCurrentUserRepositories([.. NewRepositoryJson(api, count)]);
        config.WithDiscoverAccessibleRepositories();
    }

    /// <summary>
    /// Serves <paramref name="count"/> repositories of <see cref="NewOwner"/>.
    /// </summary>
    /// <param name="api">The API stub.</param>
    /// <param name="count">The number of repositories.</param>
    /// <returns>The JSON objects of the repositories, for the list of the token's repositories.</returns>
    private static List<string> NewRepositoryJson(GitHubApiStub api, int count) =>
        Enumerable.Range(0, count)
            .Select(index => api.GivenRepository(
                _firstNewId + index,
                NewOwner,
                string.Create(CultureInfo.InvariantCulture, $"fresh-repository-{index + 1}")))
            .ToList();
}
