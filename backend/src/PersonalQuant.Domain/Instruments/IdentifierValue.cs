using System.Diagnostics.CodeAnalysis;
using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Instruments;

/// <summary>
/// A validated identifier value, in the form its scheme requires.
/// </summary>
/// <remarks>
/// <para>
/// The value and its scheme travel together because neither means anything
/// alone: <c>BBG000BLNNH6</c> is a FIGI and a nonsense ISIN, and the twelve
/// characters cannot say which was intended.
/// </para>
/// <para>
/// Validation happens once, here, at construction. Everything downstream —
/// the alias table's unique indexes, the import pipeline's deduplication, the
/// search that matches on it — assumes the value is well formed, and none of
/// them re-check it.
/// </para>
/// </remarks>
public sealed record IdentifierValue
{
    /// <summary>Length of an ISIN and of a FIGI alike.</summary>
    public const int GlobalLength = 12;

    /// <summary>Shortest accepted provider symbol.</summary>
    public const int MinProviderSymbolLength = 1;

    /// <summary>Longest accepted provider symbol.</summary>
    public const int MaxProviderSymbolLength = 32;

    /// <summary>
    /// Characters a provider is allowed to decorate a symbol with.
    /// </summary>
    /// <remarks>
    /// Vendors separate the symbol from the venue with a dot, a colon, a
    /// hyphen or a slash, and occasionally an underscore. They are preserved
    /// rather than stripped: the stored alias has to be the provider's exact
    /// spelling, or a lookup by what the provider sent will miss it.
    /// </remarks>
    private const string ProviderSymbolPunctuation = ".:-/_";

    private IdentifierValue(IdentifierScheme scheme, string value)
    {
        Scheme = scheme;
        Value = value;
    }

    /// <summary>Gets the naming system the value belongs to.</summary>
    public IdentifierScheme Scheme { get; }

    /// <summary>Gets the upper-case value.</summary>
    public string Value { get; }

    /// <summary>
    /// Creates an identifier value, throwing when it is not valid for its
    /// scheme.
    /// </summary>
    /// <param name="scheme">The naming system.</param>
    /// <param name="value">The value. Case and surrounding whitespace are normalised.</param>
    /// <returns>The parsed value.</returns>
    /// <exception cref="DomainValidationException">The value is not valid.</exception>
    public static IdentifierValue Create(IdentifierScheme scheme, string? value) =>
        TryCreate(scheme, value, out var identifier, out var problem)
            ? identifier
            : throw new DomainValidationException(problem);

    /// <summary>
    /// Attempts to create an identifier value.
    /// </summary>
    /// <param name="scheme">The naming system.</param>
    /// <param name="value">The value. Case and surrounding whitespace are normalised.</param>
    /// <param name="identifier">The parsed value when successful.</param>
    /// <param name="problem">A caller-safe explanation when validation fails.</param>
    /// <returns><see langword="true"/> when the value is valid for its scheme.</returns>
    public static bool TryCreate(
        IdentifierScheme scheme,
        string? value,
        [NotNullWhen(true)] out IdentifierValue? identifier,
        [NotNullWhen(false)] out string? problem)
    {
        identifier = null;

        if (!scheme.IsDeclared())
        {
            problem = "The identifier scheme is not one this system records.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            problem = $"A {scheme} value is required.";
            return false;
        }

        var normalised = value.Trim().ToUpperInvariant();

        var valid = scheme switch
        {
            IdentifierScheme.Isin => IsValidIsin(normalised),
            IdentifierScheme.Figi => IsValidFigi(normalised),
            _ => IsValidProviderSymbol(normalised),
        };

        if (!valid)
        {
            problem = $"'{value}' is not a valid {scheme}.";
            return false;
        }

        identifier = new IdentifierValue(scheme, normalised);
        problem = null;
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Scheme}:{Value}";

    private static bool IsValidIsin(string value)
    {
        if (value.Length != GlobalLength)
        {
            return false;
        }

        // Two-letter country prefix, then nine alphanumerics, then a digit.
        // The prefix is not checked against the ISO 3166 list: new codes are
        // issued, some ISINs use the reserved 'XS' Euroclear prefix, and a
        // stale list would reject real identifiers.
        if (!char.IsAsciiLetterUpper(value[0]) || !char.IsAsciiLetterUpper(value[1]))
        {
            return false;
        }

        for (var index = 2; index < GlobalLength - 1; index++)
        {
            if (!char.IsAsciiLetterOrDigit(value[index]))
            {
                return false;
            }
        }

        return char.IsAsciiDigit(value[^1]) && IdentifierCheckDigit.IsValidIsin(value);
    }

    private static bool IsValidFigi(string value)
    {
        if (value.Length != GlobalLength)
        {
            return false;
        }

        // The third character is always 'G'. It is the cheapest way to tell a
        // FIGI from an ISIN of the same length, and the specification fixes
        // it.
        if (value[2] != 'G')
        {
            return false;
        }

        foreach (var character in value)
        {
            // Vowels are excluded by the specification so that a FIGI cannot
            // spell a word by accident.
            if (!char.IsAsciiLetterOrDigit(character) || IsVowel(character))
            {
                return false;
            }
        }

        return char.IsAsciiDigit(value[^1]) && IdentifierCheckDigit.IsValidFigi(value);
    }

    private static bool IsValidProviderSymbol(string value)
    {
        if (value.Length is < MinProviderSymbolLength or > MaxProviderSymbolLength)
        {
            return false;
        }

        // No check digit exists, so the rule is only about what a symbol may
        // be made of. Whitespace is excluded: a symbol with a space in it is
        // almost always two fields that were concatenated by mistake.
        if (!value.All(character =>
                char.IsAsciiLetterOrDigit(character)
                || ProviderSymbolPunctuation.Contains(character, StringComparison.Ordinal)))
        {
            return false;
        }

        return char.IsAsciiLetterOrDigit(value[0]) && char.IsAsciiLetterOrDigit(value[^1]);
    }

    private static bool IsVowel(char character) =>
        character is 'A' or 'E' or 'I' or 'O' or 'U';
}
