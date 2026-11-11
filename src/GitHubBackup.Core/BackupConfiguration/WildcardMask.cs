namespace GitHubBackup.Core.BackupConfiguration;

/// <summary>
/// Matches text against a mask in which <c>*</c> stands for any sequence of characters, including none, and <c>?</c>
/// for exactly one character.
/// </summary>
/// <remarks>
/// Every other character is compared literally and without regard to case. The matcher never builds a regular
/// expression from the mask, so a mask taken from <c>backup-config.json</c> cannot change how it is interpreted and
/// the running time is bounded by the product of the lengths of the mask and the text.
/// </remarks>
internal static class WildcardMask
{
    /// <summary>
    /// Determines whether <paramref name="text"/> matches <paramref name="mask"/> as a whole.
    /// </summary>
    /// <param name="mask">The mask, which may contain <c>*</c> and <c>?</c>.</param>
    /// <param name="text">The text to test.</param>
    /// <returns><see langword="true"/> when the whole text matches the mask.</returns>
    public static bool IsMatch(string mask, string text)
    {
        var maskIndex = 0;
        var textIndex = 0;
        var starIndex = -1;
        var starTextIndex = 0;

        while (textIndex < text.Length)
        {
            if (maskIndex < mask.Length && mask[maskIndex] == '*')
            {
                // Remember the star and first try it with an empty match.
                starIndex = maskIndex++;
                starTextIndex = textIndex;
            }
            else if (maskIndex < mask.Length && (mask[maskIndex] == '?' || CharEquals(mask[maskIndex], text[textIndex])))
            {
                maskIndex++;
                textIndex++;
            }
            else if (starIndex >= 0)
            {
                // Mismatch: let the last star absorb one more character and retry after it.
                maskIndex = starIndex + 1;
                textIndex = ++starTextIndex;
            }
            else
            {
                return false;
            }
        }

        while (maskIndex < mask.Length && mask[maskIndex] == '*')
        {
            maskIndex++;
        }

        return maskIndex == mask.Length;
    }

    /// <summary>
    /// Compares two characters without regard to case.
    /// </summary>
    /// <param name="left">The first character.</param>
    /// <param name="right">The second character.</param>
    /// <returns><see langword="true"/> when the characters are equal after upper-casing.</returns>
    private static bool CharEquals(char left, char right) =>
        left == right || char.ToUpperInvariant(left) == char.ToUpperInvariant(right);
}
