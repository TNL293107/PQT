using System.Diagnostics.CodeAnalysis;
using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Instruments;

/// <summary>
/// The symbol an instrument trades under on its exchange.
/// </summary>
/// <remarks>
/// <para>
/// This is the <em>exchange</em> ticker in its canonical form — <c>FPT</c>,
/// <c>VN30F2312</c>, <c>FUEVFVND</c> — not a provider's decorated symbology
/// such as <c>FPT.HM</c> or <c>FPT:VN</c>. Mapping provider spellings onto a
/// canonical instrument is a separate concern and does not belong here.
/// </para>
/// <para>
/// A ticker is not identity. It is an attribute of an instrument that can
/// change, and can be reused by a different issuer after delisting.
/// </para>
/// </remarks>
public sealed record Ticker
{
    /// <summary>Shortest permitted ticker.</summary>
    public const int MinLength = 1;

    /// <summary>
    /// Longest permitted ticker.
    /// </summary>
    /// <remarks>
    /// Generous relative to the three characters typical of Vietnamese
    /// equities, because derivative and fund symbols are materially longer.
    /// </remarks>
    public const int MaxLength = 20;

    private Ticker(string value) => Value = value;

    /// <summary>Gets the upper-case ticker.</summary>
    public string Value { get; }

    /// <summary>
    /// Creates a ticker, throwing when the input is not valid.
    /// </summary>
    /// <param name="value">The symbol. Case and surrounding whitespace are normalised.</param>
    /// <returns>The parsed ticker.</returns>
    /// <exception cref="DomainValidationException">The value is not a valid ticker.</exception>
    public static Ticker Create(string? value) =>
        TryCreate(value, out var ticker)
            ? ticker
            : throw new DomainValidationException($"'{value}' is not a valid ticker.");

    /// <summary>
    /// Attempts to create a ticker.
    /// </summary>
    /// <param name="value">The symbol. Case and surrounding whitespace are normalised.</param>
    /// <param name="ticker">The parsed ticker when successful.</param>
    /// <returns><see langword="true"/> when the value is a valid ticker.</returns>
    public static bool TryCreate(string? value, [NotNullWhen(true)] out Ticker? ticker)
    {
        ticker = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalised = value.Trim().ToUpperInvariant();

        if (normalised.Length is < MinLength or > MaxLength)
        {
            return false;
        }

        // Alphanumeric only. Separators and suffixes are provider decoration,
        // and accepting them here would let two spellings of one security
        // become two instruments.
        if (!normalised.All(char.IsAsciiLetterOrDigit))
        {
            return false;
        }

        ticker = new Ticker(normalised);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
