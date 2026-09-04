using PersonalQuant.Domain.Exchanges;

namespace PersonalQuant.Application.Exchanges;

/// <summary>
/// Answers which days a venue was open, over a window loaded once.
/// </summary>
/// <remarks>
/// <para>
/// The reference every completeness figure rests on. Counting weekdays instead
/// would report a healthy Vietnamese series as broken every Tet, and counting
/// stored bars against nothing at all would report a series with a month
/// missing as complete.
/// </para>
/// <para>
/// Loaded for a range and then queried in memory. A quality check asks about
/// every day in a window, and a round trip per day would make the check's cost
/// proportional to the calendar rather than to the data.
/// </para>
/// </remarks>
public interface ITradingCalendar
{
    /// <summary>
    /// Loads a venue's calendar over a range, inclusive at both ends.
    /// </summary>
    /// <param name="exchangeId">The venue.</param>
    /// <param name="fromDate">The first date to cover.</param>
    /// <param name="toDate">The last date to cover.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The calendar window.</returns>
    Task<TradingCalendarWindow> LoadAsync(
        ExchangeId exchangeId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reports how far each venue's recorded calendar reaches.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The question <see cref="TradingCalendarWindow.IsComplete"/> answers for
    /// one range, asked ahead of time for every venue. Coverage running out is
    /// not a failure and produces no error: completeness simply stops being
    /// measurable, every figure computed over the uncovered period is reported
    /// as unknown, and the system carries on being correct about knowing less.
    /// </para>
    /// <para>
    /// Which is exactly why it needs asking for. A degradation that announces
    /// itself by being quietly right is one nobody notices, and Vietnam's
    /// calendar cannot be extended by inference — Tet is lunar and substitute
    /// days are set by annual decree, so the next year's coverage exists only
    /// once somebody transcribes a notice that is published late in the year
    /// before.
    /// </para>
    /// </remarks>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>Every venue, ordered by code, with the date its calendar ends.</returns>
    Task<IReadOnlyList<CalendarCoverage>> ListCoverageAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// How far one venue's recorded trading calendar reaches.
/// </summary>
/// <param name="ExchangeId">The venue.</param>
/// <param name="Code">The venue's operating code.</param>
/// <param name="Through">
/// The last date the calendar covers, or <see langword="null"/> when no
/// calendar has been recorded for this venue at all.
/// </param>
public sealed record CalendarCoverage(ExchangeId ExchangeId, ExchangeCode Code, DateOnly? Through)
{
    /// <summary>
    /// Gets a value indicating whether any calendar has been recorded.
    /// </summary>
    /// <remarks>
    /// Never recorded and run out are different states with different remedies,
    /// and neither is an empty calendar. A venue with no calendar has had no
    /// claim made about it; a venue whose calendar ended had one that expired.
    /// </remarks>
    public bool IsRecorded => Through is not null;

    /// <summary>
    /// Reports whether the calendar still covers a date.
    /// </summary>
    /// <param name="date">The date to test, normally today.</param>
    /// <returns><see langword="true"/> when coverage reaches that far.</returns>
    public bool Covers(DateOnly date) => Through is { } through && through >= date;

    /// <summary>
    /// Returns how many days of coverage remain after a date.
    /// </summary>
    /// <param name="date">The date to measure from, normally today.</param>
    /// <returns>
    /// The count, negative once coverage has lapsed, and null when no calendar
    /// has been recorded — which is not zero days of coverage.
    /// </returns>
    public int? DaysRemaining(DateOnly date) =>
        Through is { } through ? through.DayNumber - date.DayNumber : null;
}

/// <summary>
/// One venue's trading days over a range.
/// </summary>
/// <remarks>
/// <para>
/// Weekends are structural and are not stored: every venue this system covers
/// trades Monday to Friday, and recording the other two days would be tens of
/// thousands of rows asserting what the calendar already knows.
/// </para>
/// <para>
/// <see cref="IsComplete"/> is the honest part. A window whose range extends
/// past the last recorded closure cannot distinguish "no holidays there" from
/// "no holidays recorded there", and a completeness figure computed over such a
/// window will report real holidays as gaps. Callers are told rather than
/// quietly given a wrong number.
/// </para>
/// </remarks>
/// <param name="ExchangeId">The venue.</param>
/// <param name="From">The first date covered.</param>
/// <param name="To">The last date covered.</param>
/// <param name="Holidays">The scheduled closures in the range.</param>
/// <param name="IsComplete">
/// Whether the recorded calendar actually covers the range, rather than merely
/// having no closures in it.
/// </param>
public sealed record TradingCalendarWindow(
    ExchangeId ExchangeId,
    DateOnly From,
    DateOnly To,
    IReadOnlySet<DateOnly> Holidays,
    bool IsComplete)
{
    /// <summary>
    /// Reports whether the venue traded on a date.
    /// </summary>
    /// <param name="date">The date to test.</param>
    /// <returns><see langword="true"/> when the venue was open.</returns>
    public bool IsTradingDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
        && !Holidays.Contains(date);

    /// <summary>
    /// Enumerates the trading days in the window, oldest first.
    /// </summary>
    /// <returns>Every date the venue was open.</returns>
    public IEnumerable<DateOnly> TradingDays()
    {
        for (var date = From; date <= To; date = date.AddDays(1))
        {
            if (IsTradingDay(date))
            {
                yield return date;
            }
        }
    }
}

/// <summary>
/// Default <see cref="ITradingCalendar"/>.
/// </summary>
/// <param name="exchanges">The venue repository.</param>
internal sealed class TradingCalendar(IExchangeRepository exchanges) : ITradingCalendar
{
    /// <inheritdoc />
    public async Task<TradingCalendarWindow> LoadAsync(
        ExchangeId exchangeId,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        if (toDate < fromDate)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        var holidays = await exchanges
            .ListHolidaysAsync(exchangeId, fromDate, toDate, cancellationToken)
            .ConfigureAwait(false);

        // Coverage is a separate question from content. A calendar populated
        // to the end of 2026 and then asked about 2027 returns no closures,
        // which is indistinguishable from a year that genuinely had none
        // unless how far the calendar reaches is consulted too.
        var horizon = await exchanges
            .FindCalendarHorizonAsync(exchangeId, cancellationToken)
            .ConfigureAwait(false);

        return new TradingCalendarWindow(
            exchangeId,
            fromDate,
            toDate,
            holidays.Select(holiday => holiday.Date).ToHashSet(),
            horizon is { } furthest && furthest >= toDate);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarCoverage>> ListCoverageAsync(
        CancellationToken cancellationToken = default)
    {
        var venues = await exchanges.ListAsync(cancellationToken).ConfigureAwait(false);
        var coverage = new List<CalendarCoverage>(venues.Count);

        foreach (var venue in venues)
        {
            var horizon = await exchanges
                .FindCalendarHorizonAsync(venue.Id, cancellationToken)
                .ConfigureAwait(false);

            coverage.Add(new CalendarCoverage(venue.Id, venue.Code, horizon));
        }

        // One query per venue, over a table with a handful of rows. Folding it
        // into a single grouped read would be faster and would put a second
        // definition of "how far the calendar reaches" beside the one the
        // window already uses.
        return [.. coverage.OrderBy(entry => entry.Code.Value, StringComparer.Ordinal)];
    }
}
