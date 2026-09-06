namespace PersonalQuant.Application.Datasets;

/// <summary>
/// Builds the canonical research dataset PQT owns.
/// </summary>
/// <remarks>
/// <para>
/// The point of U5 in one sentence: <b>no third-party research framework may
/// become the canonical data model.</b> Frameworks consume this and hand
/// results back, so the shape a result was computed over is a fact this system
/// recorded rather than whatever a library happened to load.
/// </para>
/// <para>
/// An export reads and never writes to the system of record. It is a
/// projection of what is already stored, and one that could quietly alter the
/// series it exports would make the dataset unfalsifiable.
/// </para>
/// </remarks>
public interface IDatasetExportService
{
    /// <summary>
    /// Exports one dataset.
    /// </summary>
    /// <param name="request">The validated request.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The manifest, or why nothing was written.</returns>
    Task<DatasetExport> ExportAsync(
        DatasetRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks a dataset build still matches what its manifest claims.
    /// </summary>
    /// <remarks>
    /// Every hash is recomputed from the bytes on disk. Recording a digest and
    /// never checking it is a comment, not a guarantee.
    /// </remarks>
    /// <param name="datasetId">The dataset.</param>
    /// <param name="version">Which build of it.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>What the check found.</returns>
    Task<DatasetVerification> VerifyAsync(
        string datasetId,
        int version,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// What one export produced, or why it produced nothing.
/// </summary>
/// <remarks>
/// A refusal is a result rather than an exception. The commonest reason to
/// refuse — the universe has no membership recorded for the requested date — is
/// an ordinary state of an honest system, not a fault, and an operator needs to
/// read it rather than a stack trace.
/// </remarks>
/// <param name="Manifest">What was written, when anything was.</param>
/// <param name="Problem">Why nothing was written, when nothing was.</param>
/// <param name="InstrumentsWithoutBars">
/// Constituents the window held no stored bars for. Reported rather than
/// silently dropped: a dataset covering twenty of thirty index members is a
/// different object from one covering thirty, and only this count says which
/// was built.
/// </param>
public sealed record DatasetExport(
    DatasetManifest? Manifest,
    string? Problem,
    int InstrumentsWithoutBars = 0)
{
    /// <summary>Gets a value indicating whether a dataset was written.</summary>
    public bool Succeeded => Manifest is not null;

    /// <summary>Reports that nothing was exported, and why.</summary>
    /// <param name="problem">The caller-safe explanation.</param>
    /// <returns>A refused export.</returns>
    public static DatasetExport Refused(string problem) => new(null, problem);
}

/// <summary>
/// What checking a dataset against its manifest found.
/// </summary>
/// <param name="DatasetId">The dataset checked.</param>
/// <param name="Version">Which build of it.</param>
/// <param name="FilesChecked">How many files were hashed.</param>
/// <param name="Problems">
/// What did not match, one entry per file. Empty means the dataset is
/// byte-identical to what the manifest recorded.
/// </param>
public sealed record DatasetVerification(
    string DatasetId,
    int Version,
    int FilesChecked,
    IReadOnlyList<string> Problems)
{
    /// <summary>Gets a value indicating whether everything matched.</summary>
    public bool IsIntact => Problems.Count == 0;

    /// <summary>Reports that the dataset build could not be found.</summary>
    /// <param name="datasetId">The dataset.</param>
    /// <param name="version">Which build of it.</param>
    /// <returns>A verification that found nothing to check.</returns>
    public static DatasetVerification NotFound(string datasetId, int version) =>
        new(datasetId, version, 0, ["No manifest is stored for that dataset and version."]);
}
