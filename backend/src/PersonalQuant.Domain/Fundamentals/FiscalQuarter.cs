using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Fundamentals;

/// <summary>
/// A fiscal quarter, the point every statement value is anchored to.
/// </summary>
/// <remarks>
/// A balance is stated at the end of a quarter and a flow over a span ending
/// at one, so a quarter and a <see cref="PeriodSpan"/> together name every
/// period a Vietnamese report states. The fiscal year is the issuer's. It is
/// not assumed to be the calendar year, and no date is derived from it here.
/// </remarks>
/// <param name="Year">The fiscal year.</param>
/// <param name="Quarter">The quarter, 1 to 4.</param>
public readonly record struct FiscalQuarter(int Year, int Quarter) : IComparable<FiscalQuarter>
{
    /// <summary>The earliest fiscal year accepted.</summary>
    public const int MinYear = 1990;

    /// <summary>The latest fiscal year accepted.</summary>
    public const int MaxYear = 2100;

    /// <summary>Creates a fiscal quarter.</summary>
    /// <param name="year">The fiscal year.</param>
    /// <param name="quarter">The quarter, 1 to 4.</param>
    /// <returns>The quarter.</returns>
    /// <exception cref="DomainValidationException">Either value is out of range.</exception>
    public static FiscalQuarter Create(int year, int quarter)
    {
        if (year is < MinYear or > MaxYear)
        {
            throw new DomainValidationException(
                $"A fiscal year must be between {MinYear} and {MaxYear}, but was {year}.");
        }

        if (quarter is < 1 or > 4)
        {
            throw new DomainValidationException($"A fiscal quarter must be 1 to 4, but was {quarter}.");
        }

        return new FiscalQuarter(year, quarter);
    }

    /// <summary>Gets the same quarter of the year before.</summary>
    public FiscalQuarter YearEarlier => Create(Year - 1, Quarter);

    /// <summary>Gets the last quarter of the year before, whose closing balances open this year.</summary>
    public FiscalQuarter PriorYearEnd => Create(Year - 1, 4);

    /// <summary>Compares two quarters in time.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>Whether the first is earlier.</returns>
    public static bool operator <(FiscalQuarter left, FiscalQuarter right) => left.CompareTo(right) < 0;

    /// <summary>Compares two quarters in time.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>Whether the first is later.</returns>
    public static bool operator >(FiscalQuarter left, FiscalQuarter right) => left.CompareTo(right) > 0;

    /// <summary>Compares two quarters in time.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>Whether the first is not later.</returns>
    public static bool operator <=(FiscalQuarter left, FiscalQuarter right) => left.CompareTo(right) <= 0;

    /// <summary>Compares two quarters in time.</summary>
    /// <param name="left">The first.</param>
    /// <param name="right">The second.</param>
    /// <returns>Whether the first is not earlier.</returns>
    public static bool operator >=(FiscalQuarter left, FiscalQuarter right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public int CompareTo(FiscalQuarter other) =>
        Year != other.Year ? Year.CompareTo(other.Year) : Quarter.CompareTo(other.Quarter);

    /// <inheritdoc />
    public override string ToString() => $"{Year}Q{Quarter}";
}
