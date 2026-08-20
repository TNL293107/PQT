namespace PersonalQuant.Domain.Instruments;

/// <summary>
/// The check-digit algorithms carried by the global identifier schemes.
/// </summary>
/// <remarks>
/// <para>
/// A check digit catches a typed or transposed character, and nothing more. It
/// does not establish that an identifier exists, that it belongs to the
/// security it was filed against, or that the provider meant it. Treating a
/// passing check digit as proof of any of those would be worse than not
/// checking at all.
/// </para>
/// <para>
/// It is still worth checking. An instrument master exists so that every
/// provider's spelling of a security maps to one canonical record, and an
/// identifier with a corrupt character maps to nothing — silently, and
/// forever, because nothing downstream ever revisits it.
/// </para>
/// <para>
/// Both schemes use a double-add-double sum, and they double opposite
/// positions. The two are written out separately rather than shared behind a
/// parameter, because the parity is the part that is easy to get wrong and a
/// flag would hide which convention each one uses.
/// </para>
/// </remarks>
internal static class IdentifierCheckDigit
{
    /// <summary>Number of letters expanded into two digits each.</summary>
    private const int LetterOffset = 10;

    /// <summary>
    /// Verifies an ISO 6166 ISIN check digit.
    /// </summary>
    /// <remarks>
    /// Letters expand to their ordinal value (<c>A</c> = 10 … <c>Z</c> = 35)
    /// and the digits of that expansion are then summed with every second one
    /// doubled, counting from the right of the payload — so the digit
    /// immediately left of the check digit is itself doubled.
    /// </remarks>
    /// <param name="value">The full twelve-character identifier, upper-case.</param>
    /// <returns><see langword="true"/> when the check digit agrees.</returns>
    public static bool IsValidIsin(string value)
    {
        var expanded = Expand(value[..^1]);

        if (expanded is null)
        {
            return false;
        }

        var sum = 0;

        for (var offset = 0; offset < expanded.Count; offset++)
        {
            // offset 0 is the rightmost digit of the payload, and it is
            // doubled: the check digit occupies the position before it.
            var digit = expanded[expanded.Count - 1 - offset];

            sum += offset % 2 == 0 ? SumDigits(digit * 2) : digit;
        }

        return (10 - (sum % 10)) % 10 == value[^1] - '0';
    }

    /// <summary>
    /// Verifies an OpenFIGI check digit.
    /// </summary>
    /// <remarks>
    /// Each of the first eleven characters maps to a value (<c>0</c>–<c>9</c>,
    /// then <c>A</c> = 10 … <c>Z</c> = 35), every second one counting from the
    /// left is doubled, and the digits of the results are summed. The parity is
    /// the opposite of the ISIN's, which is the whole reason these are two
    /// methods.
    /// </remarks>
    /// <param name="value">The full twelve-character identifier, upper-case.</param>
    /// <returns><see langword="true"/> when the check digit agrees.</returns>
    public static bool IsValidFigi(string value)
    {
        var sum = 0;

        for (var index = 0; index < value.Length - 1; index++)
        {
            var mapped = MapCharacter(value[index]);

            if (mapped < 0)
            {
                return false;
            }

            sum += SumDigits(index % 2 == 1 ? mapped * 2 : mapped);
        }

        return (10 - (sum % 10)) % 10 == value[^1] - '0';
    }

    private static List<int>? Expand(string payload)
    {
        var digits = new List<int>(payload.Length * 2);

        foreach (var character in payload)
        {
            var mapped = MapCharacter(character);

            switch (mapped)
            {
                case < 0:
                    return null;
                case >= LetterOffset:
                    digits.Add(mapped / 10);
                    digits.Add(mapped % 10);
                    break;
                default:
                    digits.Add(mapped);
                    break;
            }
        }

        return digits;
    }

    private static int MapCharacter(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'A' and <= 'Z' => character - 'A' + LetterOffset,
        _ => -1,
    };

    private static int SumDigits(int value) => value < 10 ? value : (value / 10) + (value % 10);
}
