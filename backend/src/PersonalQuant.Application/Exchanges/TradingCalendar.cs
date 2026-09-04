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
    Task<IReadOnlyList<VenueCalendarCoverage>> ListCoverageAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One venue, and how far its calendar has been transcribed.
/// </summary>
/// <param name="ExchangeId">The venue.</param>
/// <param name="Code">The venue's operating code.</param>
/// <param name="Claim">
/// What the venue declares it transcribed, or <see langword="null"/> when
/// nobody has said.
/// </param>
public sealed record VenueCalendarCoverage(
    ExchangeId ExchangeId,
    ExchangeCode Code,
    CalendarCoverage? Claim)
{
    /// <summary>
    /// Gets a value indicating whether any claim has been recorded.
    /// </summary>
    /// <remarks>
    /// Never recorded and run out are different states with different remedies,
    /// and neither is an empty calendar. A venue with no claim has had nothing
    /// asserted about it; a venue whose claim ended had one that expired.
    /// </remarks>
    public bool IsDeclared => Claim is not null;

    /// <summary>Gets the last date claimed, or null when none is.</summary>
    public DateOnly? Through => Claim?.Through;

    /// <summary>
    /// Reports whether the claim covers a date.
    /// </summary>
    /// <param name="date">The date to test, normally today.</param>
    /// <returns><see langword="true"/> when the calendar was transcribed that far.</returns>
    public bool Covers(DateOnly date) => Claim?.Covers(date) ?? false;

    /// <summary>
    /// Returns how many days of transcription remain after a date.
    /// </summary>
    /// <param name="date">The date to measure from, normally today.</param>
    /// <returns>
    /// The count, negative once the claim has lapsed, and null when no claim
    /// exists or the claim runs on — neither of which is a number of days.
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

        // Coverage is a separate question from content, and it is answered by
        // the venue's recorded claim rather than by the rows.
        //
        // It used to be answered by the furthest recorded closure, and that was
        // wrong in both directions. A calendar transcribed to the end of 2026
        // reported its reach as 2 September — the year's last public holiday —
        // so the final quarter read as uncovered while its transcription sat in
        // the table. And every date *before* that closure read as covered,
        // including years nobody had transcribed: a 2016 series was checked
        // against a calendar holding no 2016 rows, and three real Vietnamese
        // public holidays were raised as missing sessions.
        //
        // Both ends of the window are tested. A figure computed over a window
        // only partly transcribed is wrong for the part that is not, and
        // nothing in the number says which part.
        var venue = await exchanges
            .FindByIdAsync(exchangeId, cancellationToken)
            .ConfigureAwait(false);

        return new TradingCalendarWindow(
            exchangeId,
            fromDate,
            toDate,
            holidays.Select(holiday => holiday.Date).ToHashSet(),
            venue?.CalendarCovers(fromDate, toDate) ?? false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VenueCalendarCoverage>> ListCoverageAsync(
        CancellationToken cancellationToken = default)
    {
        var venues = await exchanges.ListAsync(cancellationToken).ConfigureAwait(false);

        // One read, and no second query. The claim is a column on the venue,
        // which is the whole point of recording it: how far a calendar reaches
        // used to be a question you had to ask the closure rows, and the answer
        // they gave was wrong in both directions.
        return
        [
            .. venues
                .Select(venue => new VenueCalendarCoverage(
                    venue.Id, venue.Code, venue.CalendarCoverage))
                .OrderBy(entry => entry.Code.Value, StringComparer.Ordinal),
        ];
    }
}
