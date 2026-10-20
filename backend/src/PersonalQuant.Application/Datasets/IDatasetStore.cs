namespace PersonalQuant.Application.Datasets;

/// <summary>
/// One exported bar, as it is written to the research store.
/// </summary>
/// <remarks>
/// <para>
/// Keyed by the canonical identifier and not by ticker. A ticker changes on an
/// exchange transfer and is reassigned after a delisting, so a dataset joined
/// on one silently mixes two companies together — and the label a human needs
/// lives in the manifest, stamped with the instant it was valid.
/// </para>
/// <para>
/// The two factors travel with the row. Without them an adjusted dataset cannot
/// be turned back into what printed, and reconciling a research result against
/// a broker statement becomes impossible after the fact.
/// </para>
/// </remarks>
/// <param name="InstrumentId">The canonical identifier.</param>
/// <param name="OpenedAtUtc">The instant the period opened.</param>
/// <param name="Open">The first traded price, rescaled when adjusted.</param>
/// <param name="High">The highest traded price, rescaled when adjusted.</param>
/// <param name="Low">The lowest traded price, rescaled when adjusted.</param>
/// <param name="Close">The last traded price, rescaled when adjusted.</param>
/// <param name="Volume">Units traded, rescaled when adjusted.</param>
/// <param name="Turnover">Cash value traded, always as recorded.</param>
/// <param name="Source">The provider that produced it.</param>
/// <param name="Revision">Which statement of the period this is.</param>
/// <param name="PriceFactor">What the prices were multiplied by. One when raw.</param>
/// <param name="ShareFactor">What the volume was multiplied by. One when raw.</param>
public sealed record DatasetBar(
    Guid InstrumentId,
    DateTimeOffset OpenedAtUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume,
    decimal? Turnover,
    string Source,
    int Revision,
    decimal PriceFactor,
    decimal ShareFactor);

/// <summary>
/// One instrument's point-in-time ratios at one membership session, as they
/// are written to <c>fundamentals.parquet</c> (ADR-031).
/// </summary>
/// <remarks>
/// <para>
/// One computation: every field is what <c>IFundamentalRatioService</c>
/// returned for the session. A value that is absent is null, and its
/// <c>*Absent</c> companion carries the <c>RatioAbsence</c> code in
/// snake_case; the companion is null when the value is present.
/// </para>
/// <para>
/// <see cref="ReportIds"/> and <see cref="Findings"/> are JSON arrays of
/// strings, compact and in a stable order, so the file stays one string
/// column per field and both languages read the same text.
/// </para>
/// </remarks>
/// <param name="InstrumentId">The canonical identifier.</param>
/// <param name="Session">The session.</param>
/// <param name="KnownAsOfUtc">The instant statements and actions were read at.</param>
/// <param name="Scope"><c>consolidated</c> or <c>separate</c>; null when no statement is known.</param>
/// <param name="BalanceSheetQuarter">The balance sheet used, as <c>2023Q4</c>; null when none.</param>
/// <param name="ReportIds">The reports behind that balance sheet, as a JSON array.</param>
/// <param name="TtmFirstQuarter">The first of the four trailing quarters; null when they are not all known.</param>
/// <param name="TtmLastQuarter">The last of them.</param>
/// <param name="ActionsApplied">Share-count actions applied to the balance sheet's count.</param>
/// <param name="ActionsWithheld">Actions in the window not yet announced.</param>
/// <param name="Values">The nine values, in <see cref="DatasetFundamentalsRow.ValueNames"/> order.</param>
/// <param name="Findings">What the share-count checks found, as a JSON array.</param>
public sealed record DatasetFundamentalsRow(
    Guid InstrumentId,
    DateOnly Session,
    DateTimeOffset KnownAsOfUtc,
    string? Scope,
    string? BalanceSheetQuarter,
    string ReportIds,
    string? TtmFirstQuarter,
    string? TtmLastQuarter,
    int ActionsApplied,
    int ActionsWithheld,
    IReadOnlyList<(decimal? Value, string? Absent)> Values,
    string Findings)
{
    /// <summary>The value columns, each followed in the file by its <c>_absent</c> companion.</summary>
    public static readonly IReadOnlyList<string> ValueNames =
    [
        "close_raw",
        "shares",
        "net_profit_ttm",
        "eps_ttm",
        "book_value_per_share",
        "pe",
        "pb",
        "roe",
        "dividend_yield",
    ];
}

/// <summary>
/// Where exported datasets live, and how they are read back.
/// </summary>
/// <remarks>
/// <para>
/// A port because the store is files today and need not always be. U5 writes
/// Parquet to a configured directory, which needs no new infrastructure; U7
/// decides the broader storage architecture, and this seam is where that
/// decision lands without touching the export.
/// </para>
/// <para>
/// The research store is deliberately files rather than a database. A dataset
/// version that is a file with a hash can be copied, archived and verified; a
/// dataset version that is a database state cannot.
/// </para>
/// </remarks>
public interface IDatasetStore
{
    /// <summary>The file every dataset's bars are written to.</summary>
    const string BarsFileName = "bars.parquet";

    /// <summary>The file every dataset's manifest is written to.</summary>
    const string ManifestFileName = "manifest.json";

    /// <summary>The file a dataset's fundamentals are written to, when it has them.</summary>
    const string FundamentalsFileName = "fundamentals.parquet";

    /// <summary>
    /// Writes the bars of a dataset, returning what the file holds and hashes
    /// to.
    /// </summary>
    /// <param name="datasetId">The dataset the file belongs to.</param>
    /// <param name="version">Which build of that dataset.</param>
    /// <param name="bars">The rows, ordered.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The written file.</returns>
    Task<DatasetFile> WriteBarsAsync(
        string datasetId,
        int version,
        IReadOnlyList<DatasetBar> bars,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the point-in-time ratios of a dataset (ADR-031), returning what
    /// the file holds and hashes to.
    /// </summary>
    /// <param name="datasetId">The dataset the file belongs to.</param>
    /// <param name="version">Which build of that dataset.</param>
    /// <param name="rows">The rows, ordered by instrument and session.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The written file.</returns>
    Task<DatasetFile> WriteFundamentalsAsync(
        string datasetId,
        int version,
        IReadOnlyList<DatasetFundamentalsRow> rows,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the manifest beside the data it describes.
    /// </summary>
    /// <param name="manifest">The manifest to write.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    Task WriteManifestAsync(
        DatasetManifest manifest,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a manifest back, or null when no such dataset build exists.
    /// </summary>
    /// <param name="datasetId">The dataset.</param>
    /// <param name="version">Which build of it.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The manifest, or null.</returns>
    Task<DatasetManifest?> ReadManifestAsync(
        string datasetId,
        int version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the builds of a dataset that exist, oldest version first.
    /// </summary>
    /// <param name="datasetId">The dataset.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The versions present.</returns>
    Task<IReadOnlyList<int>> ListVersionsAsync(
        string datasetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recomputes the digest of a file that was written for a dataset build.
    /// </summary>
    /// <param name="datasetId">The dataset.</param>
    /// <param name="version">Which build of it.</param>
    /// <param name="fileName">The file, relative to the build's directory.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The digest, or null when the file is gone.</returns>
    Task<string?> ComputeHashAsync(
        string datasetId,
        int version,
        string fileName,
        CancellationToken cancellationToken = default);
}
