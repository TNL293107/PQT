namespace PersonalQuant.Application.Instruments;

/// <summary>
/// Why an import refused a provider row.
/// </summary>
/// <remarks>
/// Enumerated so rejections can be counted and compared between sources.
/// "Four hundred rows rejected" is noise; "four hundred rows rejected because
/// the venue was unknown" is a missing exchange seed.
/// </remarks>
public enum InstrumentImportRejectionReason
{
    /// <summary>The symbol could not be split into a ticker.</summary>
    UnreadableSymbol = 1,

    /// <summary>The name was missing or unusable.</summary>
    UnusableName = 2,

    /// <summary>
    /// Neither the row nor its symbol named a venue this system knows.
    /// </summary>
    /// <remarks>
    /// Not a defect in the row so much as a gap in reference data. The
    /// exchange has to exist before an instrument can point at it.
    /// </remarks>
    UnknownExchange = 3,

    /// <summary>An identifier the row carried was malformed.</summary>
    InvalidIdentifier = 4,

    /// <summary>
    /// The row's identifiers point at one instrument and its symbol at
    /// another.
    /// </summary>
    /// <remarks>
    /// The rejection that matters most. Resolving it either way would merge
    /// two securities or split one, and both are the kind of quiet corruption
    /// the instrument master exists to prevent. It is left for a human.
    /// </remarks>
    ConflictingIdentity = 5,

    /// <summary>The row repeated a symbol already seen in the same import.</summary>
    DuplicateWithinImport = 6,
}

/// <summary>
/// One provider row that did not become or match an instrument, and why.
/// </summary>
/// <param name="Row">The row as the source reported it.</param>
/// <param name="Reason">Why it was refused.</param>
/// <param name="Detail">A short, specific explanation.</param>
public sealed record InstrumentImportRejection(
    ProviderInstrument Row,
    InstrumentImportRejectionReason Reason,
    string Detail);

/// <summary>
/// What one import run did.
/// </summary>
/// <remarks>
/// <para>
/// The counts are separate because they mean different things. Created is new
/// securities; matched is rows that resolved to an instrument already held and
/// changed nothing; enriched is rows that filled in something missing. A run
/// that creates a thousand instruments on its second execution has failed to
/// deduplicate, and only the split between created and matched says so.
/// </para>
/// <para>
/// There is no "deleted" and no "deactivated". A security absent from a
/// provider's list has not necessarily delisted — the vendor may have dropped
/// coverage, or the file may be truncated — and inferring a lifecycle
/// transition from an absence is how a live security silently disappears.
/// </para>
/// </remarks>
/// <param name="Source">The provider that was read.</param>
/// <param name="RowsRead">How many rows the source returned.</param>
/// <param name="Created">Securities the master did not previously hold.</param>
/// <param name="Matched">Rows that resolved to an existing instrument.</param>
/// <param name="Enriched">Existing instruments that gained a fact they lacked.</param>
/// <param name="AliasesRecorded">Aliases written, across created and matched rows.</param>
/// <param name="Rejections">Rows that were refused, with reasons.</param>
public sealed record InstrumentImportReport(
    string Source,
    int RowsRead,
    int Created,
    int Matched,
    int Enriched,
    int AliasesRecorded,
    IReadOnlyList<InstrumentImportRejection> Rejections)
{
    /// <summary>Gets how many rows were refused.</summary>
    public int Rejected => Rejections.Count;

    /// <summary>
    /// A report for a source that could not be read at all.
    /// </summary>
    /// <param name="source">The provider that was attempted.</param>
    /// <returns>An empty report.</returns>
    public static InstrumentImportReport Empty(string source) =>
        new(source, 0, 0, 0, 0, 0, []);
}
