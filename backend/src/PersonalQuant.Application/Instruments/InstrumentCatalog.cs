using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// Default <see cref="IInstrumentCatalog"/>.
/// </summary>
/// <remarks>
/// Thin, like the search service: the work is the query, and this is the seam
/// callers depend on. What it does add is the guard against an unassigned
/// identifier, so a caller that has lost its selection asks the database
/// nothing at all.
/// </remarks>
/// <param name="instruments">The instrument master.</param>
internal sealed class InstrumentCatalog(IInstrumentRepository instruments) : IInstrumentCatalog
{
    /// <inheritdoc />
    public Task<InstrumentDetail?> FindDetailAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        id.IsEmpty
            ? Task.FromResult<InstrumentDetail?>(null)
            : instruments.FindDetailByIdAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<InstrumentPage> ListAsync(
        InstrumentListCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(criteria);

        return instruments.ListAsync(criteria, cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RelatedInstrument>> ListRelatedAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        id.IsEmpty
            ? Task.FromResult<IReadOnlyList<RelatedInstrument>>([])
            : instruments.ListRelatedAsync(id, cancellationToken);
}
