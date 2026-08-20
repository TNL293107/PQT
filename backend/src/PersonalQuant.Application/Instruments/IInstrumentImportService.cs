using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// Populates the instrument master from an external source.
/// </summary>
/// <remarks>
/// The pipeline the roadmap describes:
/// <c>provider → import → normalize symbol → deduplicate → instrument master</c>.
/// It is the step that makes the master's promise true — that every provider's
/// spelling of a security maps to one canonical identifier — because it is
/// where the spellings are recorded.
/// </remarks>
public interface IInstrumentImportService
{
    /// <summary>
    /// Reads a source and reconciles it against the instrument master.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Creates what is missing, matches what is not, and records the source's
    /// symbol as an alias either way. Nothing is deleted and nothing is
    /// delisted: a security absent from a provider's list has not necessarily
    /// stopped trading, and inferring a lifecycle transition from an absence
    /// is how a live security silently disappears.
    /// </para>
    /// <para>
    /// Rows that cannot be reconciled are returned as rejections rather than
    /// thrown. One malformed row must not stop the other four thousand from
    /// being imported.
    /// </para>
    /// </remarks>
    /// <param name="source">
    /// The provider to read, or <see langword="null"/> to use the only one
    /// registered.
    /// </param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What the run did.</returns>
    /// <exception cref="MarketDataProviderException">The source could not be read.</exception>
    /// <exception cref="InvalidOperationException">No such source is registered.</exception>
    Task<InstrumentImportReport> ImportAsync(
        SourceCode? source,
        CancellationToken cancellationToken = default);
}
