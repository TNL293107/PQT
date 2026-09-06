using PersonalQuant.Domain.CorporateActions;

namespace PersonalQuant.Application.Datasets;

/// <summary>
/// One file in an exported dataset, with what it holds and what it hashes to.
/// </summary>
/// <remarks>
/// The hash is the point. A dataset whose file no longer matches its manifest
/// is a hard error on load, because a reproducible result computed from a
/// silently changed input is worse than no result at all.
/// </remarks>
/// <param name="Name">The file name, relative to the dataset directory.</param>
/// <param name="RowCount">Rows written.</param>
/// <param name="Bytes">Size on disk.</param>
/// <param name="Sha256">Lowercase hex digest of the file's bytes.</param>
public sealed record DatasetFile(string Name, long RowCount, long Bytes, string Sha256);

/// <summary>
/// One instrument in the exported set, with the label it carried at the
/// universe's as-of.
/// </summary>
/// <remarks>
/// The identifier is the identity and the ticker is a label. Tickers are not
/// stable — they change on an exchange transfer and are reassigned after a
/// delisting — so the ticker appears here, stamped with the instant it was
/// valid, and nowhere in the data rows. Nothing can then be joined on it by
/// accident.
/// </remarks>
/// <param name="InstrumentId">The canonical identifier.</param>
/// <param name="TickerAtAsOf">What it was called at the universe's as-of.</param>
/// <param name="Exchange">The venue's operating code.</param>
public sealed record DatasetInstrument(Guid InstrumentId, string TickerAtAsOf, string Exchange);

/// <summary>
/// What a dataset is, what produced it, and what it must still hash to.
/// </summary>
/// <remarks>
/// <para>
/// The contract PQT owns. No third-party research framework may become the
/// canonical data model — frameworks consume this and hand results back — and
/// a contract that lived inside one of them would be that framework's contract
/// rather than this system's.
/// </para>
/// <para>
/// Everything a result depends on is named here, because a dataset that cannot
/// say which rules produced it cannot be reproduced when those rules change.
/// That includes the two versions the bars carry, the adjustment policy, and
/// the observation instant — a figure computed from an as-of read is not
/// comparable with one computed from today's series, and nothing about the
/// numbers says which is which.
/// </para>
/// </remarks>
/// <param name="DatasetId">
/// Derived from the parameters rather than issued at random, so the same
/// request names the same dataset. See <see cref="DatasetIdentity"/>.
/// </param>
/// <param name="DatasetVersion">
/// Which build of this dataset it is. Two exports of the same parameters
/// against a database that has since been corrected are the same
/// <paramref name="DatasetId"/> at different versions.
/// </param>
/// <param name="SchemaVersion">
/// Which manifest shape this is. A loader that does not know the version
/// refuses rather than guessing which fields it can trust.
/// </param>
/// <param name="ContentHash">
/// The reproducible identity: a digest over every field except
/// <paramref name="DatasetVersion"/>, <paramref name="CreatedAtUtc"/> and
/// <paramref name="CreatedByCommit"/> — the three that say which run this was
/// rather than what the data is. Two exports of identical parameters over
/// identical data agree on this and disagree on the manifest's bytes, because
/// one of them happened later.
/// </param>
/// <param name="KnownAsOfUtc">
/// The observation instant the bars were read at, or null for the current
/// series. Null is a legitimate export and a different thing from a
/// point-in-time one.
/// </param>
/// <param name="Adjusted">Whether prices were rescaled for corporate actions.</param>
/// <param name="AnnouncementPolicy">
/// What the read did with an action whose announcement date is unknown. A
/// dataset built permissively carries look-ahead into every experiment run
/// against it, which is why the export names it rather than assuming it.
/// </param>
/// <param name="UniverseCode">The universe the instrument set came from.</param>
/// <param name="UniverseAsOf">The date the constituent set was read at.</param>
/// <param name="Instruments">The exported set, by canonical identifier.</param>
/// <param name="Interval">The bar resolution.</param>
/// <param name="FromDate">First session in the window, inclusive.</param>
/// <param name="ToDate">Last session in the window, inclusive.</param>
/// <param name="TransformationVersion">Normalisation rules the bars passed through.</param>
/// <param name="ValidationVersion">Quality rules the bars were checked by.</param>
/// <param name="AdjustmentVersion">Rules that produced the factors applied.</param>
/// <param name="Sources">
/// Every provider code that contributed a row, with the licence note the
/// deployment records against it. A dataset that cannot say where its rows came
/// from cannot answer whether it may be redistributed.
/// </param>
/// <param name="Files">The data files, with their hashes.</param>
/// <param name="CreatedAtUtc">When this export ran.</param>
/// <param name="CreatedByCommit">
/// The build that produced it, or null when the build did not record one.
/// Null rather than a placeholder: a dataset that claims a commit it does not
/// know is worse than one that admits it.
/// </param>
public sealed record DatasetManifest(
    string DatasetId,
    int DatasetVersion,
    int SchemaVersion,
    string ContentHash,
    DateTimeOffset? KnownAsOfUtc,
    bool Adjusted,
    AnnouncementPolicy AnnouncementPolicy,
    string UniverseCode,
    DateOnly UniverseAsOf,
    IReadOnlyList<DatasetInstrument> Instruments,
    string Interval,
    DateOnly FromDate,
    DateOnly ToDate,
    int TransformationVersion,
    int ValidationVersion,
    int AdjustmentVersion,
    IReadOnlyList<DatasetSource> Sources,
    IReadOnlyList<DatasetFile> Files,
    DateTimeOffset CreatedAtUtc,
    string? CreatedByCommit)
{
    /// <summary>
    /// The manifest shape this build writes and knows how to read.
    /// </summary>
    /// <remarks>
    /// Version 1: one Parquet file of bars keyed by instrument identifier, a
    /// universe-derived instrument set, and the three rule versions the rows
    /// were produced under.
    /// </remarks>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Gets the total rows across every file.</summary>
    public long RowCount => Files.Sum(file => file.RowCount);
}

/// <summary>
/// A provider that contributed rows, and what this deployment may do with
/// them.
/// </summary>
/// <remarks>
/// Recorded per dataset rather than looked up later. The terms under which a
/// source was read are a fact about the export; a note fetched afterwards
/// describes whatever the configuration happens to say today.
/// </remarks>
/// <param name="Code">The provider code carried on the rows.</param>
/// <param name="LicenceNote">
/// What the deployment records about redistributing this source's data.
/// </param>
public sealed record DatasetSource(string Code, string LicenceNote);
