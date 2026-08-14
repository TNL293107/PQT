using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Diagnostics;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// Default <see cref="IInstrumentSearchService"/>.
/// </summary>
/// <remarks>
/// Thin on purpose. Ranking and filtering belong in the query the database
/// executes, not in a layer above it, so what is left here is the seam every
/// caller depends on and the observability that seam is the right place for.
/// </remarks>
/// <param name="instruments">The instrument master.</param>
/// <param name="logger">Logger for search telemetry.</param>
internal sealed class InstrumentSearchService(
    IInstrumentRepository instruments,
    ILogger<InstrumentSearchService> logger) : IInstrumentSearchService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<InstrumentSearchResult>> SearchAsync(
        InstrumentSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        // Search is a per-keystroke path, so the timing is only taken when
        // something is listening for it.
        var measuring = logger.IsEnabled(LogLevel.Debug);
        var started = measuring ? Stopwatch.GetTimestamp() : 0L;

        var results = await instruments
            .SearchAsync(criteria, cancellationToken)
            .ConfigureAwait(false);

        if (measuring)
        {
            // Hoisted into a local: the analyzer that guards against expensive
            // logging arguments cannot see through the call otherwise.
            var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            ApplicationLog.InstrumentSearchCompleted(
                logger, results.Count, criteria.Text.Length, elapsedMs);
        }

        return results;
    }
}
