namespace GitHubBackup.Core.History;

/// <summary>
/// Defines who took the decision on a discovery event.
/// </summary>
public enum DecisionMaker
{
    /// <summary>The user answered a question.</summary>
    User,

    /// <summary>The automatic strategy of silent mode decided.</summary>
    Automatic,
}
