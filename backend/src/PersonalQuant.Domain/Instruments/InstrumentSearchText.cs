using System.Globalization;
using System.Text;

namespace PersonalQuant.Domain.Instruments;

/// <summary>
/// Normalises free text so that a query and an instrument's stored search
/// fields can be compared with a single, ordinal rule.
/// </summary>
/// <remarks>
/// <para>
/// Both sides of a search — the user's query and the values persisted on
/// <see cref="Instrument"/> — pass through here. That is the point: once both
/// are folded to the same form, matching is plain ordinal comparison, which
/// behaves identically in the CLR and in PostgreSQL. Case-insensitive or
/// accent-insensitive collations would put the answer at the mercy of the
/// database's locale, and prefix matching could no longer use a plain index.
/// </para>
/// <para>
/// Diacritics are folded because Vietnamese company names carry them and
/// nobody types them into a terminal. <c>Công ty Cổ phần FPT</c> has to be
/// reachable by <c>cong ty</c>. The letter <c>Đ</c> is handled explicitly:
/// unlike the accented vowels it is a distinct letter rather than a base plus
/// a combining mark, so Unicode decomposition leaves it untouched.
/// </para>
/// <para>
/// Punctuation is preserved. Removing it would make <c>FPT.</c> and <c>FPT</c>
/// the same token but also silently change which names count as an exact
/// match, and no query in practice depends on it.
/// </para>
/// </remarks>
public static class InstrumentSearchText
{
    /// <summary>
    /// Folds text to its searchable form: diacritics removed, upper-cased
    /// invariantly, and internal whitespace collapsed to single spaces.
    /// </summary>
    /// <param name="value">The text to fold. May be <see langword="null"/>.</param>
    /// <returns>
    /// The folded text, or an empty string when the input is null, empty or
    /// only whitespace.
    /// </returns>
    public static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // FormD splits a precomposed character such as 'ế' into its base
        // letter plus combining marks, so the marks can simply be dropped.
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var separatorPending = false;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(character))
            {
                // Deferred rather than appended, so leading and trailing
                // whitespace never reach the output.
                separatorPending = builder.Length > 0;
                continue;
            }

            if (separatorPending)
            {
                builder.Append(' ');
                separatorPending = false;
            }

            builder.Append(Fold(char.ToUpperInvariant(character)));
        }

        return builder.ToString();
    }

    private static char Fold(char upperCased) => upperCased switch
    {
        // Đ decomposes to nothing: it is its own letter, not D plus a stroke.
        'Đ' => 'D',
        _ => upperCased,
    };
}
