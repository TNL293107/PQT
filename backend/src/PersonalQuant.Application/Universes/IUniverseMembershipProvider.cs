using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.Universes;

/// <summary>
/// An external source of universe definitions and membership history.
/// </summary>
/// <remarks>
/// <para>
/// Separate from the instrument and corporate action sources because it is a
/// different publication. Vietnamese index membership is announced in review
/// notices rather than served on an endpoint, and a deployment may well have
/// prices, actions and no membership history at all — which is worth knowing
/// rather than papering over.
/// </para>
/// <para>
/// The source states its own coverage. Nothing else can: whether a file
/// contains every review since 2018 or only the ones somebody had time to
/// transcribe is knowledge the operator has and the rows do not carry.
/// </para>
/// </remarks>
public interface IUniverseMembershipProvider
{
    /// <summary>Gets the code membership from this source is attributed to.</summary>
    SourceCode Code { get; }

    /// <summary>
    /// Reads the universes the source defines.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The universe definitions, each with the span it claims.</returns>
    /// <exception cref="MarketData.MarketDataProviderException">
    /// The source could not be read.
    /// </exception>
    Task<IReadOnlyList<ProviderUniverse>> ListUniversesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads every membership spell the source knows about.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The spells.</returns>
    /// <exception cref="MarketData.MarketDataProviderException">
    /// The source could not be read.
    /// </exception>
    Task<IReadOnlyList<ProviderUniverseMembership>> ListMembershipsAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One universe exactly as a source defined it.
/// </summary>
/// <remarks>
/// The coverage dates are the operator's claim about the file, not a fact
/// derived from its rows. Leaving them empty is honest and has a consequence:
/// every as-of read against that universe is unanswerable until somebody states
/// what the file is supposed to contain.
/// </remarks>
/// <param name="Code">The universe's short code.</param>
/// <param name="Name">The full name.</param>
/// <param name="Kind">What kind of set it is, by name.</param>
/// <param name="CoverageFrom">The first date the source claims to cover.</param>
/// <param name="CoverageUntil">
/// The first date it no longer claims, or null while it is maintained.
/// </param>
public sealed record ProviderUniverse(
    string Code,
    string Name,
    string Kind,
    DateOnly? CoverageFrom = null,
    DateOnly? CoverageUntil = null);

/// <summary>
/// One membership spell exactly as a source reported it.
/// </summary>
/// <remarks>
/// The symbol rather than an identifier: a source names securities in its own
/// symbology, and resolving that to a canonical instrument is the import's job.
/// </remarks>
/// <param name="UniverseCode">The universe the security belonged to.</param>
/// <param name="Symbol">The source's spelling of the symbol.</param>
/// <param name="EffectiveFrom">The first date of membership.</param>
/// <param name="EffectiveTo">
/// The first date of non-membership, or null while the security still belongs.
/// </param>
/// <param name="AnnouncedOn">When the change was published, when the source states it.</param>
public sealed record ProviderUniverseMembership(
    string UniverseCode,
    string Symbol,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo = null,
    DateOnly? AnnouncedOn = null);

/// <summary>
/// Why a membership row could not be recorded.
/// </summary>
public enum UniverseMembershipRejectionReason
{
    /// <summary>The row names a universe the source does not define.</summary>
    UnknownUniverse = 1,

    /// <summary>The symbol could not be resolved to an instrument.</summary>
    UnknownInstrument = 2,

    /// <summary>The spell would cover no session.</summary>
    EmptyInterval = 3,

    /// <summary>
    /// The spell overlaps one already recorded for the same security.
    /// </summary>
    /// <remarks>
    /// Refused here rather than at the exclusion constraint, so the rejection
    /// can name the security and the two spells. The constraint is still what
    /// makes the rule true of the table; this only makes the failure legible.
    /// </remarks>
    OverlapsRecordedSpell = 4,

    /// <summary>
    /// The row restates a spell that is already recorded as ended, with a
    /// different ending.
    /// </summary>
    /// <remarks>
    /// A spell that has ended is a fact about the past. A source changing its
    /// mind about one is a real event and needs a decision, not a silent
    /// rewrite — the previous ending has already been read by anything that ran
    /// a backtest against it.
    /// </remarks>
    ContradictsRecordedSpell = 5,

    /// <summary>The same spell appeared more than once in one import.</summary>
    DuplicateWithinImport = 6,
}

/// <summary>One membership row that was refused, and why.</summary>
/// <param name="Row">The row as the source reported it.</param>
/// <param name="Reason">Why it was refused.</param>
/// <param name="Detail">A short, specific explanation.</param>
public sealed record UniverseMembershipRejection(
    ProviderUniverseMembership Row,
    UniverseMembershipRejectionReason Reason,
    string Detail);

/// <summary>
/// What one universe import did.
/// </summary>
/// <param name="Source">The source that was read.</param>
/// <param name="UniversesDefined">Universes recorded for the first time.</param>
/// <param name="CoverageDeclared">Universes whose coverage claim was set or moved.</param>
/// <param name="RowsRead">Membership rows the source returned.</param>
/// <param name="SpellsCreated">Spells recorded for the first time.</param>
/// <param name="SpellsClosed">Open spells the source reported as ended.</param>
/// <param name="Unchanged">Spells already held exactly as reported.</param>
/// <param name="Rejections">Rows that were refused, with reasons.</param>
/// <param name="Coverage">What the coverage review found afterwards.</param>
public sealed record UniverseImportReport(
    string Source,
    int UniversesDefined,
    int CoverageDeclared,
    int RowsRead,
    int SpellsCreated,
    int SpellsClosed,
    int Unchanged,
    IReadOnlyList<UniverseMembershipRejection> Rejections,
    UniverseCoverageReport Coverage)
{
    /// <summary>Gets how many rows were refused.</summary>
    public int Rejected => Rejections.Count;
}

/// <summary>
/// Populates the universe record from an external source, then reviews what is
/// still missing from it.
/// </summary>
public interface IUniverseImportService
{
    /// <summary>
    /// Reads the configured source and reconciles it against what is held.
    /// </summary>
    /// <remarks>
    /// Additive and idempotent. A spell already held exactly as reported is
    /// left alone; an open spell the source now reports as ended is closed.
    /// Nothing is deleted — a source that has stopped publishing a spell may
    /// simply have shortened its window, and inferring a removal from an
    /// absence would silently rewrite which securities a strategy could have
    /// chosen from.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What the run did.</returns>
    /// <exception cref="MarketData.MarketDataProviderException">
    /// The source could not be read.
    /// </exception>
    /// <exception cref="InvalidOperationException">No source is registered.</exception>
    Task<UniverseImportReport> ImportAsync(CancellationToken cancellationToken = default);
}
