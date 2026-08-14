namespace PersonalQuant.Application.Instruments;

/// <summary>
/// Finds instruments by ticker or name.
/// </summary>
/// <remarks>
/// The entry point for instrument discovery. Callers that already know which
/// security they mean want <see cref="IInstrumentResolver"/> instead — this
/// one answers "what could the user have meant?", which is a different
/// question and returns a ranked list rather than an answer.
/// </remarks>
public interface IInstrumentSearchService
{
    /// <summary>
    /// Returns instruments matching the query, strongest match first.
    /// </summary>
    /// <remarks>
    /// The order is total and deterministic: match kind, then ticker, then
    /// identifier. Two calls with the same criteria against unchanged data
    /// return the same list in the same order.
    /// </remarks>
    /// <param name="criteria">The validated query.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// Ranked results, never more than <see cref="InstrumentSearchCriteria.Limit"/>,
    /// and empty when nothing matches.
    /// </returns>
    Task<IReadOnlyList<InstrumentSearchResult>> SearchAsync(
        InstrumentSearchCriteria criteria,
        CancellationToken cancellationToken = default);
}
