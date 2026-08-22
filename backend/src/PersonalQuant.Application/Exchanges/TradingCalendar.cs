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
}
