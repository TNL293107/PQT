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
}
