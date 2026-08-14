namespace PersonalQuant.Infrastructure.Persistence;

/// <summary>
/// Builds <c>LIKE</c> patterns from user-supplied text.
/// </summary>
/// <remarks>
/// <para>
/// The text itself always reaches PostgreSQL as a bound parameter, so this is
/// not about SQL injection. It is about a caller being able to change what a
/// query <em>means</em>: an unescaped <c>%</c> in a search box turns a prefix
/// match into a wildcard scan of the whole table, and an unescaped <c>_</c>
/// silently matches characters the user did not type.
/// </para>
/// <para>
/// Escaping here rather than stripping the characters keeps the search honest.
/// A user who types <c>%</c> is looking for a name containing a percent sign,
/// and will find one.
/// </para>
/// </remarks>
internal static class LikePattern
{
    /// <summary>
    /// The escape character declared to PostgreSQL alongside every pattern
    /// this type builds.
    /// </summary>
    public const string EscapeCharacter = "\\";

    /// <summary>Builds a pattern matching values that begin with the text.</summary>
    /// <param name="text">The literal text to match.</param>
    /// <returns>A <c>LIKE</c> pattern.</returns>
    public static string StartsWith(string text) => Escape(text) + "%";

    /// <summary>Builds a pattern matching values that contain the text.</summary>
    /// <param name="text">The literal text to match.</param>
    /// <returns>A <c>LIKE</c> pattern.</returns>
    public static string Contains(string text) => "%" + Escape(text) + "%";

    private static string Escape(string text) =>
        // The escape character is doubled first. Doing it later would also
        // escape the backslashes this method has just introduced.
        text.Replace(EscapeCharacter, EscapeCharacter + EscapeCharacter, StringComparison.Ordinal)
            .Replace("%", EscapeCharacter + "%", StringComparison.Ordinal)
            .Replace("_", EscapeCharacter + "_", StringComparison.Ordinal);
}
