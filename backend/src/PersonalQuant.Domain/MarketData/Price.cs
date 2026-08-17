using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.MarketData;

/// <summary>
/// A traded price, in the instrument's quote currency.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="decimal"/> rather than <see cref="double"/>, and not
/// negotiable. Binary floating point cannot represent a tenth exactly, so a
/// price stored as a double comes back very slightly different from the one
/// that went in; summed across a backtest that error compounds into returns
/// the market never produced.
/// </para>
/// <para>
/// A price is strictly positive. Zero is not a price a trade happened at — it
/// is a provider's way of saying "no data", and accepting it would put a bar
/// into the series that every indicator built on top would treat as a
/// hundred-per-cent drawdown.
/// </para>
/// <para>
/// The scale is bounded so that the stored value and the value in memory are
/// the same number. Rounding at persistence rather than at construction would
/// mean a bar that passed its invariant check in the CLR could violate it
/// after a round trip.
/// </para>
/// </remarks>
public readonly record struct Price : IComparable<Price>
{
    /// <summary>
    /// Digits permitted after the decimal point.
    /// </summary>
    /// <remarks>
    /// Vietnamese equities quote in whole dong, but index levels and fund
    /// NAVs carry decimals, and adjusted series produced in Phase 4 carry
    /// more. Six is comfortably beyond any of them and still exact in
    /// <c>numeric(18,6)</c>.
    /// </remarks>
    public const int MaxScale = 6;

    /// <summary>
    /// Largest price the system will accept.
    /// </summary>
    /// <remarks>
    /// A bound rather than a business rule: it is the point past which a value
    /// is certainly a unit error or a corrupt field rather than a quote, and
    /// it keeps every product of a price inside <c>numeric(18,6)</c>.
    /// </remarks>
    public const decimal MaxValue = 1_000_000_000_000m;

    private Price(decimal value) => Value = value;

    /// <summary>Gets the price.</summary>
    public decimal Value { get; }

    /// <summary>Converts a price to its underlying decimal.</summary>
    /// <param name="price">The price to convert.</param>
    public static implicit operator decimal(Price price) => price.Value;

    /// <summary>Reports whether one price is below another.</summary>
    /// <param name="left">The left price.</param>
    /// <param name="right">The right price.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is lower.</returns>
    public static bool operator <(Price left, Price right) => left.Value < right.Value;

    /// <summary>Reports whether one price is above another.</summary>
    /// <param name="left">The left price.</param>
    /// <param name="right">The right price.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is higher.</returns>
    public static bool operator >(Price left, Price right) => left.Value > right.Value;

    /// <summary>Reports whether one price is at or below another.</summary>
    /// <param name="left">The left price.</param>
    /// <param name="right">The right price.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is not higher.</returns>
    public static bool operator <=(Price left, Price right) => left.Value <= right.Value;

    /// <summary>Reports whether one price is at or above another.</summary>
    /// <param name="left">The left price.</param>
    /// <param name="right">The right price.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is not lower.</returns>
    public static bool operator >=(Price left, Price right) => left.Value >= right.Value;

    /// <summary>
    /// Creates a price, throwing when the value is not one.
    /// </summary>
    /// <param name="value">The price.</param>
    /// <returns>The parsed price.</returns>
    /// <exception cref="DomainValidationException">The value is not a valid price.</exception>
    public static Price Create(decimal value) =>
        TryCreate(value, out var price)
            ? price
            : throw new DomainValidationException(
                $"{value.ToString(CultureInfo.InvariantCulture)} is not a valid price.");

    /// <summary>
    /// Attempts to create a price.
    /// </summary>
    /// <param name="value">The price.</param>
    /// <param name="price">The parsed price when successful.</param>
    /// <returns><see langword="true"/> when the value is a valid price.</returns>
    public static bool TryCreate(decimal value, [NotNullWhen(true)] out Price price)
    {
        price = default;

        if (value is <= 0m or > MaxValue)
        {
            return false;
        }

        if (value.Scale > MaxScale)
        {
            return false;
        }

        price = new Price(value);
        return true;
    }

    /// <inheritdoc />
    public int CompareTo(Price other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
