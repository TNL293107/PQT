using System.Globalization;
using Microsoft.Extensions.Logging;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Diagnostics;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.Datasets;

/// <summary>
/// Default <see cref="IDatasetExportService"/>.
/// </summary>
/// <remarks>
/// <para>
/// An orchestration and nothing more. It resolves who was in the universe,
/// reads each one's series through the same query path the terminal uses, and
/// hands the rows to the store. Nothing here re-implements a read: a dataset
/// whose bars came from a second code path would eventually disagree with the
/// chart drawn from the first, and there would be no way to say which was
/// right.
/// </para>
/// <para>
/// Reads are chunked because a series read is bounded by construction. The
/// chunk is sized from the resolution so a chunk cannot hold more periods than
/// the bound allows, which keeps the paging arithmetic independent of how many
/// sessions a venue actually traded.
/// </para>
/// </remarks>
/// <param name="universes">Resolves who belonged to the universe.</param>
/// <param name="instruments">Supplies the label each identifier carried.</param>
/// <param name="marketData">Reads the series, through the one query path.</param>
/// <param name="store">Where datasets are written and read back.</param>
/// <param name="licences">What may be done with each source's rows.</param>
/// <param name="build">Identifies the build that produced the export.</param>
/// <param name="clock">Supplies the creation instant.</param>
/// <param name="logger">Logger for export telemetry.</param>
internal sealed class DatasetExportService(
    IUniverseCatalog universes,
    IInstrumentRepository instruments,
    IMarketDataQueryService marketData,
    IDatasetStore store,
    IDatasetLicenceRegistry licences,
    IBuildIdentity build,
    IClock clock,
    ILogger<DatasetExportService> logger) : IDatasetExportService
{
    /// <inheritdoc />
    public async Task<DatasetExport> ExportAsync(
        DatasetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var constituents = await universes
            .ConstituentsAsOfAsync(request.UniverseCode, request.UniverseAsOf, cancellationToken)
            .ConfigureAwait(false);

        // The refusal U2 exists to make possible. An unknown membership is not
        // an empty one, and exporting a dataset over "nobody" would produce a
        // backtest that reports no positions and no error.
        if (!constituents.IsKnown)
        {
            return DatasetExport.Refused(
                $"Membership of {request.UniverseCode} on "
                + $"{request.UniverseAsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} is not "
                + $"known ({constituents.UnknownReason}). Declare the universe's coverage and import "
                + "its history before exporting a dataset over it.");
        }

        if (constituents.Members.Count == 0)
        {
            return DatasetExport.Refused(
                $"{request.UniverseCode} had no constituents on "
                + $"{request.UniverseAsOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}, so there "
                + "is nothing to export.");
        }

        var rows = new List<DatasetBar>();
        var members = new List<DatasetInstrument>();
        var sources = new SortedSet<string>(StringComparer.Ordinal);
        var withoutBars = 0;

        foreach (var instrumentId in constituents.Members)
        {
            var detail = await instruments
                .FindDetailByIdAsync(instrumentId, cancellationToken)
                .ConfigureAwait(false);

            // A constituent the instrument master no longer holds is a broken
            // reference rather than a company with no prices, and exporting its
            // bars under no label would produce rows nobody can identify.
            if (detail is null)
            {
                return DatasetExport.Refused(
                    $"Instrument {instrumentId} is a constituent of {request.UniverseCode} but is not "
                    + "in the instrument master. The universe import and the instrument master "
                    + "disagree, and a dataset built over that disagreement cannot be interpreted.");
            }

            members.Add(new DatasetInstrument(
                instrumentId.Value, detail.Ticker.Value, detail.ExchangeCode.Value));

            var before = rows.Count;

            await ReadSeriesAsync(request, instrumentId, rows, sources, cancellationToken)
                .ConfigureAwait(false);

            if (rows.Count == before)
            {
                withoutBars++;
            }
        }

        if (rows.Count == 0)
        {
            return DatasetExport.Refused(
                "No bars are stored for any constituent over that window, so the export would be an "
                + "empty dataset. Ingest the series first.");
        }

        // Ordered once, here, so the file's bytes depend on the data and not on
        // the order the database happened to return it in. Without it two
        // exports of identical data would hash differently.
        rows.Sort(static (left, right) =>
        {
            var byInstrument = left.InstrumentId.CompareTo(right.InstrumentId);
            return byInstrument != 0 ? byInstrument : left.OpenedAtUtc.CompareTo(right.OpenedAtUtc);
        });

        var existing = await store
            .ListVersionsAsync(request.DatasetId, cancellationToken)
            .ConfigureAwait(false);

        var version = existing.Count == 0 ? 1 : existing.Max() + 1;

        var file = await store
            .WriteBarsAsync(request.DatasetId, version, rows, cancellationToken)
            .ConfigureAwait(false);

        var manifest = Describe(request, version, members, sources, file);

        await store.WriteManifestAsync(manifest, cancellationToken).ConfigureAwait(false);

        ApplicationLog.DatasetExported(
            logger,
            manifest.DatasetId,
            version,
            members.Count,
            file.RowCount,
            withoutBars);

        return new DatasetExport(manifest, null, withoutBars);
    }

    /// <inheritdoc />
    public async Task<DatasetVerification> VerifyAsync(
        string datasetId,
        int version,
        CancellationToken cancellationToken = default)
    {
        var manifest = await store
            .ReadManifestAsync(datasetId, version, cancellationToken)
            .ConfigureAwait(false);

        if (manifest is null)
        {
            return DatasetVerification.NotFound(datasetId, version);
        }

        var problems = new List<string>();

        foreach (var file in manifest.Files)
        {
            var actual = await store
                .ComputeHashAsync(datasetId, version, file.Name, cancellationToken)
                .ConfigureAwait(false);

            if (actual is null)
            {
                problems.Add($"{file.Name} is named by the manifest and is not on disk.");
                continue;
            }

            if (!string.Equals(actual, file.Sha256, StringComparison.Ordinal))
            {
                problems.Add(
                    $"{file.Name} hashes to {actual} but the manifest records {file.Sha256}.");
            }
        }

        // The manifest can be edited as easily as the data it describes, so its
        // own content hash is recomputed too. Checking only the files would let
        // somebody change the as-of a dataset claims and leave every hash
        // matching.
        var content = DatasetIdentity.ContentHashOf(manifest);

        if (!string.Equals(content, manifest.ContentHash, StringComparison.Ordinal))
        {
            problems.Add(
                $"The manifest hashes to {content} but records {manifest.ContentHash}; its own "
                + "fields have been changed since the export.");
        }

        return new DatasetVerification(datasetId, version, manifest.Files.Count, problems);
    }

    /// <summary>
    /// Reads one instrument's series across the window, in bounded chunks.
    /// </summary>
    private async Task ReadSeriesAsync(
        DatasetRequest request,
        InstrumentId instrumentId,
        List<DatasetBar> rows,
        SortedSet<string> sources,
        CancellationToken cancellationToken)
    {
        var chunk = ChunkDays(request.Interval);
        var cursor = request.FromDate;

        // The window is inclusive of its last day and the query's is not, so
        // the exclusive end is the day after.
        var end = request.ToDate.AddDays(1);

        while (cursor < end)
        {
            var next = cursor.AddDays(chunk);

            if (next > end)
            {
                next = end;
            }

            if (!BarQuery.TryCreate(
                    instrumentId,
                    request.Interval,
                    ToInstant(cursor),
                    ToInstant(next),
                    BarQuery.MaxLimit,
                    out var query,
                    out var problem,
                    request.Adjusted,
                    request.KnownAsOfUtc,
                    request.AnnouncementPolicy))
            {
                // Unreachable from a validated request, and thrown rather than
                // swallowed: silently exporting a short series would produce a
                // dataset that looks complete and is not.
                throw new InvalidOperationException(
                    $"An export chunk produced an invalid bar query: {problem}");
            }

            var series = await marketData
                .GetSeriesAsync(query, cancellationToken)
                .ConfigureAwait(false);

            foreach (var bar in series.Bars)
            {
                sources.Add(bar.Source.Value);

                rows.Add(new DatasetBar(
                    instrumentId.Value,
                    bar.OpenedAtUtc,
                    bar.Open,
                    bar.High,
                    bar.Low,
                    bar.Close,
                    bar.Volume,
                    bar.Turnover,
                    bar.Source.Value,
                    bar.Revision,
                    bar.PriceFactor,
                    bar.ShareFactor));
            }

            cursor = next;
        }
    }

    private DatasetManifest Describe(
        DatasetRequest request,
        int version,
        IReadOnlyList<DatasetInstrument> members,
        IEnumerable<string> sources,
        DatasetFile file)
    {
        var manifest = new DatasetManifest(
            request.DatasetId,
            version,
            DatasetManifest.CurrentSchemaVersion,
            // Filled in below: the hash covers the fields, so it cannot be one
            // of the fields it covers.
            string.Empty,
            request.KnownAsOfUtc,
            request.Adjusted,
            request.AnnouncementPolicy,
            request.UniverseCode.Value,
            request.UniverseAsOf,
            members,
            BarIntervalParser.Describe(request.Interval),
            request.FromDate,
            request.ToDate,
            DataRules.TransformationVersion,
            DataRules.ValidationVersion,
            DataRules.AdjustmentVersion,
            [.. sources.Select(code => new DatasetSource(code, licences.NoteFor(code)))],
            [file],
            clock.UtcNow,
            build.Commit);

        return manifest with { ContentHash = DatasetIdentity.ContentHashOf(manifest) };
    }

    /// <summary>
    /// How many days one read may cover without exceeding the query bound.
    /// </summary>
    /// <remarks>
    /// Derived from the resolution rather than fixed, and deliberately
    /// pessimistic: a calendar day cannot hold more periods than a full
    /// twenty-four hours of them, so a chunk of this many days cannot exceed
    /// the bound however many sessions the venue traded.
    /// </remarks>
    private static int ChunkDays(BarInterval interval)
    {
        var periodsPerDay = Math.Max(
            1, (int)(TimeSpan.FromDays(1).TotalMinutes / interval.ToDuration().TotalMinutes));

        return Math.Max(1, BarQuery.MaxLimit / periodsPerDay);
    }

    private static DateTimeOffset ToInstant(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
}
