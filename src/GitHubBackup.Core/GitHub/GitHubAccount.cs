namespace GitHubBackup.Core.GitHub;

/// <summary>
/// A GitHub account as the server reports it.
/// </summary>
/// <param name="Login">The login in the letter case the server uses.</param>
/// <param name="Type">The kind of account.</param>
public sealed record GitHubAccount(string Login, GitHubAccountType Type);
