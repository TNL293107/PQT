using Microsoft.EntityFrameworkCore;
using PersonalQuant.Application.Quotes;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IQuoteReader"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each read is one query over the whole member set: <c>instrument_id = ANY
/// (@ids)</c> on the leading column of the table's key, so the bar and factor
/// reads are index range scans however many members there are.
/// </para>
/// <para>
/// Entities are materialised and projected in memory rather than projected in
/// SQL. Prices, sources and factors reach their columns through value
/// converters, and a projection of a member of a converted value is a query
/// that compiles and throws the first time it meets PostgreSQL — the rows here
/// are a few hundred at most.
/// </para>
/// </remarks>
/// <param name="dbContext">The unit of work to read through.</param>
internal sealed class QuoteReader(PersonalQuantDbContext dbContext) : IQuoteReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<QuoteInstrument>> ListInstrumentsAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);

        var ids = instrumentIds.Distinct().ToList();

        var instruments = await dbContext.Instruments
            .AsNoTracking()
            .Where(instrument => ids.Contains(instrument.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. instruments.Select(instrument =>
                new QuoteInstrument(instrument.Id, instrument.Ticker, instrument.ExchangeId, instrument.AssetType)),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionClose>> ListDailyClosesAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);

        var ids = instrumentIds.Distinct().ToList();

        // A daily bar opens at midnight UTC of its session date, the shape
        // every source normalises to, so the window is the half-open range of
        // instants from the first session to the day after the last.
        var fromUtc = Midnight(fromDate);
        var toUtc = Midnight(toDate.AddDays(1));

        var bars = await dbContext.Bars
            .AsNoTracking()
            .Where(bar =>
                ids.Contains(bar.InstrumentId)
                && bar.Interval == BarInterval.OneDay
                && bar.OpenedAtUtc >= fromUtc
                && bar.OpenedAtUtc < toUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. bars.Select(bar => new SessionClose(
                bar.InstrumentId,
                DateOnly.FromDateTime(bar.OpenedAtUtc.UtcDateTime),
                bar.Close,
                bar.Volume,
                bar.Source)),
        ];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ExDateFactor>> ListFactorsGoingExAsync(
        IReadOnlyCollection<InstrumentId> instrumentIds,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instrumentIds);

        var ids = instrumentIds.Distinct().ToList();

        var adjustments = await dbContext.PriceAdjustments
            .AsNoTracking()
            .Where(adjustment =>
                ids.Contains(adjustment.InstrumentId)
                && adjustment.ExDate >= fromDate
                && adjustment.ExDate <= toDate)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. adjustments.Select(adjustment =>
                new ExDateFactor(adjustment.InstrumentId, adjustment.ExDate, adjustment.Factor)),
        ];
    }

    private static DateTimeOffset Midnight(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
