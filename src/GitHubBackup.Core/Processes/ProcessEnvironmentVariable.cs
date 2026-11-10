using System.Text;

namespace GitHubBackup.Core.Processes;

/// <summary>
/// An environment variable set for one child process.
/// </summary>
/// <remarks>
/// The value never reaches the log: <see cref="IProcessRunner"/> logs only the name, and <see cref="object.ToString"/>
/// leaves the value out.
/// </remarks>
/// <param name="Name">The name of the variable.</param>
/// <param name="Value">The value of the variable.</param>
/// <param name="IsSecret">
/// <see langword="true"/> when the value is a secret, such as a header that carries the token; the runner then masks
/// the value in every output line of the process.
/// </param>
public sealed record ProcessEnvironmentVariable(string Name, string Value, bool IsSecret = false)
{
    /// <summary>
    /// Writes the name and the secret flag, but not the value, into the text of <see cref="object.ToString"/>.
    /// </summary>
    /// <param name="builder">The builder of the text.</param>
    /// <returns><see langword="true"/>, because members were written.</returns>
    /// <remarks>
    /// The compiler-generated <see cref="object.ToString"/> of the record calls this method in place of the generated
    /// one, which would print every property.
    /// </remarks>
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(nameof(Name)).Append(" = ").Append(Name)
            .Append(", ").Append(nameof(IsSecret)).Append(" = ").Append(IsSecret);
        return true;
    }
}
