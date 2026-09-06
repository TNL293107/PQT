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
