using PersonalQuant.Domain.Common;

namespace PersonalQuant.Domain.Exchanges;

/// <summary>
/// A date on which a venue is scheduled not to trade.
/// </summary>
/// <remarks>
/// <para>
/// The reference data that separates a hole in a series from a day the market
/// was shut. Without it, completeness cannot be measured at all: every public
/// holiday looks exactly like a failed ingestion run, and a quality score
/// computed against a bare weekday count would report a healthy series as
/// broken every Tet.
/// </para>
/// <para>
/// Weekends are not stored here. Saturday and Sunday are structural for every
/// venue this system covers, so recording them would be tens of thousands of
/// rows asserting something the calendar already knows.
/// </para>
/// <para>
/// A holiday is per venue rather than per country. Vietnamese venues currently
/// close together, but that is an observation about today's market and not a
/// rule — a venue-specific closure such as a systems outage has to be
/// expressible without inventing a national holiday.
/// </para>
/// </remarks>
public sealed class TradingHoliday : AuditableEntity
{
    /// <summary>Longest permitted description.</summary>
    public const int MaxNameLength = 120;

    // EF Core materialises through this constructor and sets the properties
    // directly; it is never used by application code.
    private TradingHoliday() => Name = null!;

    private TradingHoliday(ExchangeId exchangeId, DateOnly date, string name)
    {
        ExchangeId = exchangeId;
        Date = date;
        Name = name;
    }

    /// <summary>Gets the venue that is closed.</summary>
    public ExchangeId ExchangeId { get; private set; }

    /// <summary>
    /// Gets the closed date, in the venue's local calendar.
    /// </summary>
    /// <remarks>
    /// A <see cref="DateOnly"/>, not an instant. A holiday is a date in the
    /// venue's own calendar and turning it into a UTC timestamp would make it
    /// land on a different day for anyone reading it from another offset.
    /// </remarks>
    public DateOnly Date { get; private set; }

    /// <summary>Gets what the closure is, such as <c>National Day</c>.</summary>
    public string Name { get; private set; }

    /// <summary>
    /// Records a scheduled closure.
    /// </summary>
    /// <param name="exchangeId">The venue that is closed.</param>
    /// <param name="date">The closed date.</param>
    /// <param name="name">What the closure is.</param>
    /// <param name="occurredAtUtc">The instant the record is created.</param>
    /// <returns>The new holiday.</returns>
    /// <exception cref="DomainValidationException">A supplied value is invalid.</exception>
    public static TradingHoliday Record(
        ExchangeId exchangeId,
        DateOnly date,
        string name,
        DateTimeOffset occurredAtUtc)
    {
        if (exchangeId.IsEmpty)
        {
            throw new DomainValidationException("A trading holiday must belong to an exchange.");
        }

        var holiday = new TradingHoliday(exchangeId, date, RequireName(name));

        holiday.MarkCreated(occurredAtUtc);
        return holiday;
    }

    private static string RequireName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            // An unnamed closure cannot be audited later. "Why was the market
            // shut on this date?" is exactly the question this row exists to
            // answer.
            throw new DomainValidationException("A trading holiday must say what it is.");
        }

        var trimmed = name.Trim();

        return trimmed.Length > MaxNameLength
            ? throw new DomainValidationException(
                $"A trading holiday name may not exceed {MaxNameLength} characters.")
            : trimmed;
    }
}
