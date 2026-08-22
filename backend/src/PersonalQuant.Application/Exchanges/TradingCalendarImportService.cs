using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Diagnostics;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Exchanges;

namespace PersonalQuant.Application.Exchanges;

/// <summary>
/// Default <see cref="ITradingCalendarImportService"/>.
/// </summary>
/// <remarks>
/// One transaction for the whole run, and additive only. A half-applied
/// calendar is worse than none: the horizon would move forward past dates whose
/// closures were never written, and every one of them would then be reported as
/// a missing session.
/// </remarks>
/// <param name="providers">Every registered calendar source.</param>
/// <param name="exchanges">The venue repository.</param>
/// <param name="unitOfWork">Commits the run.</param>
/// <param name="clock">Supplies the audit timestamps.</param>
/// <param name="logger">Logger for import telemetry.</param>
internal sealed class TradingCalendarImportService(
    IEnumerable<ITradingCalendarProvider> providers,
    IExchangeRepository exchanges,
    IUnitOfWork unitOfWork,
    IClock clock,
    ILogger<TradingCalendarImportService> logger) : ITradingCalendarImportService
{
    /// <inheritdoc />
    public async Task<TradingCalendarImportReport> ImportAsync(
        CancellationToken cancellationToken = default)
    {
        var registered = providers.ToList();

        var provider = registered.Count == 1
            ? registered[0]
            : throw new InvalidOperationException(
                registered.Count == 0
                    ? "No trading calendar source is registered."
                    : "Several trading calendar sources are registered, which is not supported.");

        var rows = await provider
            .ListHolidaysAsync(cancellationToken)
            .ConfigureAwait(false);

        var occurredAtUtc = clock.UtcNow;
        var venues = new Dictionary<string, ExchangeId?>(StringComparer.Ordinal);
        var staged = new HashSet<(ExchangeId Venue, DateOnly Date)>();
        var rejections = new List<string>();
        var created = 0;
        var alreadyHeld = 0;

        foreach (var row in rows)
        {
            var venue = await ResolveAsync(venues, row.ExchangeCode, cancellationToken)
                .ConfigureAwait(false);

            if (venue is null)
            {
                rejections.Add(
                    $"'{row.ExchangeCode}' on {row.Date:yyyy-MM-dd} names a venue this system does not hold.");
                continue;
            }

            // Both the database and this run are consulted. The first stops a
            // closure recorded last time being written again; the second stops
            // a file that lists one date twice from tripping the primary key.
            if (!staged.Add((venue.Value, row.Date)))
            {
                alreadyHeld++;
                continue;
            }

            var held = await exchanges
                .HasHolidayAsync(venue.Value, row.Date, cancellationToken)
                .ConfigureAwait(false);

            if (held)
            {
                alreadyHeld++;
                continue;
            }

            try
            {
                exchanges.AddHoliday(
                    TradingHoliday.Record(venue.Value, row.Date, row.Name, occurredAtUtc));

                created++;
            }
            catch (DomainValidationException exception)
            {
                // One unusable row must not stop a year of calendar from being
                // recorded.
                rejections.Add($"{row.ExchangeCode} on {row.Date:yyyy-MM-dd}: {exception.Message}");
                staged.Remove((venue.Value, row.Date));
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        ApplicationLog.TradingCalendarImported(
            logger, provider.Code.Value, rows.Count, created, alreadyHeld, rejections.Count);

        return new TradingCalendarImportReport(
            provider.Code.Value, rows.Count, created, alreadyHeld, rejections);
    }

    private async Task<ExchangeId?> ResolveAsync(
        Dictionary<string, ExchangeId?> cache,
        string? code,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code) || !ExchangeCode.TryCreate(code, out var parsed))
        {
            return null;
        }

        if (cache.TryGetValue(parsed.Value, out var cached))
        {
            return cached;
        }

        var exchange = await exchanges
            .FindByCodeAsync(parsed, cancellationToken)
            .ConfigureAwait(false);

        cache[parsed.Value] = exchange?.Id;
        return exchange?.Id;
    }
}
