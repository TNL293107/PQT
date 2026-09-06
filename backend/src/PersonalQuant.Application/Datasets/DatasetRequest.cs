using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.Application.Datasets;

/// <summary>
/// A validated request to export a canonical dataset.
/// </summary>
/// <remarks>
/// <para>
/// Bounded by construction. An export walks every instrument in a universe
/// across a date range, which is the largest read this system performs, and one
/// asked for a century of one-minute bars would be a way to make the database
/// do unbounded work.
/// </para>
/// <para>
/// The announcement policy defaults to <see cref="AnnouncementPolicy.Strict"/>
/// here and to permissive on the chart, and the difference is deliberate. A
/// dataset is built to be run against repeatedly by things nobody has written
/// yet; a chart is looked at once. Look-ahead baked into a dataset propagates
/// into every experiment that reads it and shows up as a better result, which
/// nobody investigates.
/// </para>
/// </remarks>
public sealed record DatasetRequest
{
    /// <summary>Longest window an export may cover.</summary>
    public const int MaxYears = 30;

    private DatasetRequest(
        UniverseCode universeCode,
        DateOnly universeAsOf,
        BarInterval interval,
        DateOnly fromDate,
        DateOnly toDate,
        bool adjusted,
        DateTimeOffset? knownAsOfUtc,
        AnnouncementPolicy announcementPolicy)
    {
        UniverseCode = universeCode;
        UniverseAsOf = universeAsOf;
        Interval = interval;
        FromDate = fromDate;
        ToDate = toDate;
        Adjusted = adjusted;
        KnownAsOfUtc = knownAsOfUtc;
        AnnouncementPolicy = announcementPolicy;
    }

    /// <summary>Gets the universe the instrument set comes from.</summary>
    public UniverseCode UniverseCode { get; }

    /// <summary>Gets the date the constituent set is read at.</summary>
    public DateOnly UniverseAsOf { get; }

    /// <summary>Gets the bar resolution.</summary>
    public BarInterval Interval { get; }

    /// <summary>Gets the first session, inclusive.</summary>
    public DateOnly FromDate { get; }

    /// <summary>Gets the last session, inclusive.</summary>
    public DateOnly ToDate { get; }

    /// <summary>Gets whether prices are rescaled for corporate actions.</summary>
    public bool Adjusted { get; }

    /// <summary>Gets the observation instant, or null for the current series.</summary>
    public DateTimeOffset? KnownAsOfUtc { get; }

    /// <summary>Gets what to do with an unknown announcement date.</summary>
    public AnnouncementPolicy AnnouncementPolicy { get; }

    /// <summary>
    /// Gets the identifier these parameters name.
    /// </summary>
    /// <remarks>
    /// Derived, not issued. A random identifier would make the same request
    /// name a different dataset every time it ran, and "did we already build
    /// this?" would be unanswerable without comparing every field by hand.
    /// </remarks>
    public string DatasetId => DatasetIdentity.Of(this);

    /// <summary>
    /// Validates an export request.
    /// </summary>
    /// <param name="universeCode">The universe to take the instrument set from.</param>
    /// <param name="universeAsOf">The date to read the constituent set at.</param>
    /// <param name="interval">The bar resolution.</param>
    /// <param name="fromDate">The first session, inclusive.</param>
    /// <param name="toDate">The last session, inclusive.</param>
    /// <param name="request">The validated request when successful.</param>
    /// <param name="problem">A caller-safe explanation when validation fails.</param>
    /// <param name="adjusted">Whether to rescale for corporate actions.</param>
    /// <param name="knownAsOfUtc">The observation instant, or null.</param>
    /// <param name="announcementPolicy">What to do with an unknown announcement date.</param>
    /// <returns><see langword="true"/> when the request is usable.</returns>
    public static bool TryCreate(
        UniverseCode universeCode,
        DateOnly universeAsOf,
        BarInterval interval,
        DateOnly fromDate,
        DateOnly toDate,
        [NotNullWhen(true)] out DatasetRequest? request,
        [NotNullWhen(false)] out string? problem,
        bool adjusted = true,
        DateTimeOffset? knownAsOfUtc = null,
        AnnouncementPolicy announcementPolicy = AnnouncementPolicy.Strict)
    {
        request = null;

        if (universeCode is null)
        {
            problem = "A universe is required.";
            return false;
        }

        if (!interval.IsDeclared())
        {
            problem = "The bar resolution is not one this system records.";
            return false;
        }

        if (!announcementPolicy.IsDeclared())
        {
            problem = "The announcement policy is not one this system understands.";
            return false;
        }

        if (toDate < fromDate)
        {
            problem = "The window must end on or after it starts.";
            return false;
        }

        if (fromDate.AddYears(MaxYears) < toDate)
        {
            problem = $"The window may not span more than {MaxYears} years.";
            return false;
        }

        // A constituent set read after the window it describes would export the
        // membership of a later index against earlier prices, which is
        // survivorship bias assembled by hand.
        if (universeAsOf > toDate)
        {
            problem =
                "The universe must be read on or before the window ends, or the export applies a "
                + "later membership to earlier prices.";
            return false;
        }

        request = new DatasetRequest(
            universeCode,
            universeAsOf,
            interval,
            fromDate,
            toDate,
            adjusted,
            knownAsOfUtc?.ToUniversalTime(),
            announcementPolicy);
        problem = null;
        return true;
    }
}

/// <summary>
/// Derives a dataset's identifier and content hash from what produced it.
/// </summary>
/// <remarks>
/// Both are digests over a canonical rendering of their inputs, so the same
/// inputs give the same answer on any machine and in any culture. The
/// invariant-culture formatting is not decoration: a dataset identifier that
/// depended on the operator's locale would be a reproducibility bug that only
/// appeared on somebody else's laptop.
/// </remarks>
public static class DatasetIdentity
{
    /// <summary>Characters of the digest kept for an identifier.</summary>
    private const int IdLength = 16;

    /// <summary>
    /// Names the dataset a request produces.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>A short lowercase hex identifier.</returns>
    public static string Of(DatasetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canonical = string.Join(
            '|',
            request.UniverseCode.Value,
            Format(request.UniverseAsOf),
            request.Interval.ToString(),
            Format(request.FromDate),
            Format(request.ToDate),
            request.Adjusted ? "adjusted" : "raw",
            request.KnownAsOfUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "current",
            request.AnnouncementPolicy.ToString());

        return Digest(canonical)[..IdLength];
    }

    /// <summary>
    /// Computes the reproducible identity of a manifest.
    /// </summary>
    /// <remarks>
    /// Covers every field except the dataset version, the creation instant and
    /// the build that ran it — the three that say <em>which run</em> rather
    /// than <em>what the data is</em>. That is what makes it reproducible: two
    /// exports of identical parameters over identical data agree here and
    /// differ in their manifest bytes, because one of them happened later.
    /// </remarks>
    /// <param name="manifest">The manifest, whose content hash is ignored.</param>
    /// <returns>A lowercase hex digest.</returns>
    public static string ContentHashOf(DatasetManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        // Deliberately excludes DatasetVersion along with the two timestamps.
        // Version says which build this is, not what the data is, so including
        // it would make every rebuild disagree with the one before it — and the
        // property this hash exists to provide is exactly that they agree.
        var builder = new StringBuilder()
            .Append(manifest.DatasetId).Append('|')
            .Append(manifest.SchemaVersion).Append('|')
            .Append(manifest.KnownAsOfUtc?.ToString("O", CultureInfo.InvariantCulture) ?? "current").Append('|')
            .Append(manifest.Adjusted).Append('|')
            .Append(manifest.AnnouncementPolicy).Append('|')
            .Append(manifest.UniverseCode).Append('|')
            .Append(Format(manifest.UniverseAsOf)).Append('|')
            .Append(manifest.Interval).Append('|')
            .Append(Format(manifest.FromDate)).Append('|')
            .Append(Format(manifest.ToDate)).Append('|')
            .Append(manifest.TransformationVersion).Append('|')
            .Append(manifest.ValidationVersion).Append('|')
            .Append(manifest.AdjustmentVersion).Append('|');

        foreach (var instrument in manifest.Instruments)
        {
            builder.Append(instrument.InstrumentId).Append(',');
        }

        builder.Append('|');

        foreach (var source in manifest.Sources)
        {
            builder.Append(source.Code).Append(',');
        }

        builder.Append('|');

        foreach (var file in manifest.Files)
        {
            builder.Append(file.Name).Append(':').Append(file.Sha256).Append(',');
        }

        return Digest(builder.ToString());
    }

    private static string Format(DateOnly date) =>
        date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string Digest(string canonical) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
}
