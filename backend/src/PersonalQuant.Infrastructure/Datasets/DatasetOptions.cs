using System.ComponentModel.DataAnnotations;

namespace PersonalQuant.Infrastructure.Datasets;

/// <summary>
/// Where the research store lives and what this deployment may do with each
/// source's rows.
/// </summary>
/// <remarks>
/// U5 writes Parquet to a configured directory, which needs no infrastructure
/// this deployment does not already have. U7 decides whether the research store
/// should be anything more than files.
/// </remarks>
public sealed class DatasetOptions
{
    /// <summary>The configuration section these options bind to.</summary>
    public const string SectionName = "Datasets";

    /// <summary>
    /// Gets the directory exported datasets are written under.
    /// </summary>
    /// <remarks>
    /// A dataset build lives at <c>{Directory}/{datasetId}/v{version}/</c>. The
    /// path is per-deployment because a research store on a developer's laptop
    /// and one in a container are different volumes, and neither should be
    /// compiled in.
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string Directory { get; init; } = "data/datasets";

    /// <summary>
    /// Gets the note to record against each provider code, keyed by code.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A source not named here is recorded as unstated, which says plainly that
    /// no licence was granted and the rows are not for redistribution. That is
    /// the true position for every Vietnamese source this system reads: the
    /// data is retained because nothing prohibits retaining it, and an absence
    /// of prohibition is not a permission.
    /// </para>
    /// <para>
    /// Configured rather than compiled in, because the terms are a fact about a
    /// deployment's arrangements and not about this code. A deployment that
    /// obtains a real licence records it here, and every dataset built
    /// afterwards carries it.
    /// </para>
    /// </remarks>
    public IDictionary<string, string> LicenceNotes { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
