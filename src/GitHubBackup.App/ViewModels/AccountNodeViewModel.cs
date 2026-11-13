namespace GitHubBackup.App.ViewModels;

/// <summary>
/// Backs the node of a tracked account, whose children are the repositories listed under it.
/// </summary>
/// <param name="login">The login of the account.</param>
/// <param name="repositories">The nodes of the repositories of the account, in file order.</param>
/// <param name="selectionChanged">Receives the node and its new selection state.</param>
public sealed class AccountNodeViewModel(
    string login, IReadOnlyList<RepositoryNodeViewModel> repositories, Action<TreeNodeViewModel, bool> selectionChanged)
    : TreeNodeViewModel(CreateKey(login), selectionChanged)
{
    /// <summary>Gets the login of the account.</summary>
    public string Login { get; } = login;

    /// <summary>Gets the nodes of the repositories of the account.</summary>
    public IReadOnlyList<RepositoryNodeViewModel> Repositories { get; } = repositories;

    /// <inheritdoc />
    public override string DisplayName => Login;

    /// <inheritdoc />
    public override IReadOnlyList<TreeNodeViewModel> Children => Repositories;

    /// <summary>
    /// Returns the key of the node of an account; GitHub logins ignore case, so the key does too.
    /// </summary>
    /// <param name="login">The login of the account.</param>
    /// <returns>The key.</returns>
    public static string CreateKey(string login) => $"account:{login.ToUpperInvariant()}";
}
