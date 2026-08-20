using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// Reads the instrument master by identifier rather than by query text.
/// </summary>
/// <remarks>
/// The third of the three reads over the same table, and the one for a caller
/// that already knows which security it means.
/// <see cref="IInstrumentSearchService"/> answers "what could the user have
/// meant?"; <see cref="IInstrumentResolver"/> answers "which security is this
/// symbol?"; this one answers "tell me everything about this one".
/// </remarks>
public interface IInstrumentCatalog
{
    /// <summary>
    /// Reads one instrument in full, including its venue and classification.
    /// </summary>
    /// <param name="id">The canonical identifier.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The instrument, or <see langword="null"/> when unknown.</returns>
    Task<InstrumentDetail?> FindDetailAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Pages through the instrument master.
    /// </summary>
    /// <param name="criteria">The validated request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The page, and how many rows match in total.</returns>
    Task<InstrumentPage> ListAsync(
        InstrumentListCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the instruments connected to one by identity.
    /// </summary>
    /// <param name="id">The instrument to relate from.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>
    /// The related instruments, empty when there are none and when the
    /// identifier is unknown — the two are distinguished by first reading the
    /// instrument itself.
    /// </returns>
    Task<IReadOnlyList<RelatedInstrument>> ListRelatedAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default);
}
