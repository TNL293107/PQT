using System.Diagnostics.CodeAnalysis;
using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Currencies;

/// <summary>
/// An ISO 4217 alphabetic currency code.
/// </summary>
/// <remarks>
/// Modelled as a type rather than a <see cref="string"/> so that a currency
/// can never be silently swapped for a ticker, a name, or an empty value on
/// its way into the database.
/// </remarks>
public sealed record CurrencyCode
{
    /// <summary>Length of an ISO 4217 alphabetic code.</summary>
    public const int Length = 3;

    /// <summary>The Vietnamese dong, the currency of every HOSE, HNX and UPCOM listing.</summary>
    public static readonly CurrencyCode Vnd = new("VND");

    private CurrencyCode(string value) => Value = value;

    /// <summary>Gets the upper-case three-letter code.</summary>
    public string Value { get; }

    /// <summary>
    /// Creates a currency code, throwing when the input is not valid.
    /// </summary>
    /// <param name="value">A three-letter code. Case and surrounding whitespace are normalised.</param>
    /// <returns>The parsed currency code.</returns>
    /// <exception cref="DomainValidationException">The value is not three letters.</exception>
    public static CurrencyCode Create(string? value) =>
        TryCreate(value, out var currency)
            ? currency
            : throw new DomainValidationException(
                $"'{value}' is not a valid ISO 4217 currency code.");

    /// <summary>
    /// Attempts to create a currency code.
    /// </summary>
    /// <param name="value">A three-letter code. Case and surrounding whitespace are normalised.</param>
    /// <param name="currency">The parsed code when successful.</param>
    /// <returns><see langword="true"/> when the value is a valid code.</returns>
    public static bool TryCreate(string? value, [NotNullWhen(true)] out CurrencyCode? currency)
    {
        currency = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalised = value.Trim().ToUpperInvariant();

        if (normalised.Length != Length || !normalised.All(char.IsAsciiLetterUpper))
        {
            return false;
        }

        currency = new CurrencyCode(normalised);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
