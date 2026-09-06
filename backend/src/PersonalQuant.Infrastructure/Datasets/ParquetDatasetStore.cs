using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Parquet;
using Parquet.Schema;
using PersonalQuant.Application.Datasets;

namespace PersonalQuant.Infrastructure.Datasets;

/// <summary>
/// The research store: Parquet files with a JSON manifest, on a configured
/// directory.
/// </summary>
/// <remarks>
/// <para>
/// Files rather than a database, deliberately. A dataset version that is a file
/// with a hash can be copied, archived and verified anywhere; a dataset version
/// that is a database state cannot be handed to anybody, and cannot be shown to
/// be the same one a result was computed from.
/// </para>
/// <para>
/// Prices are written as strings, not doubles. A decimal price forced through
/// binary floating point is no longer the number that was stored — 47500.1
/// becomes 47500.099999999999, and every reconciliation against a broker
/// statement afterwards is a comparison of two things that are not equal.
/// Parquet's own decimal type would preserve the value, but pins a precision
/// and a scale into the file format, and an adjusted price can carry a far
/// deeper scale than any real price. The text is exact, reversible, and
/// hashes deterministically.
/// </para>
/// </remarks>
/// <param name="options">Where to write and what to record about each source.</param>
internal sealed class ParquetDatasetStore(IOptions<DatasetOptions> options)
    : IDatasetStore, IDatasetLicenceRegistry
{
    /// <summary>
    /// How a manifest is written, exposed so the schema test can hold the two
    /// to each other rather than describing the shape a second time.
    /// </summary>
    internal static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // Lowercased, so the manifest names a policy the way the API query
        // string and the operator CLI do. A file that said "Strict" where every
        // other surface says "strict" would be one more thing to translate.
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    private readonly DatasetOptions settings = options?.Value
        ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc />
    public async Task<DatasetFile> WriteBarsAsync(
        string datasetId,
        int version,
        IReadOnlyList<DatasetBar> bars,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bars);

        var directory = Build(datasetId, version);
        System.IO.Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, IDatasetStore.BarsFileName);
        var schema = BarSchema();

        await using (var stream = File.Create(path))
        {
            await using var writer = await ParquetWriter
                .CreateAsync(schema, stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            using var group = writer.CreateRowGroup();

            await WriteColumnsAsync(group, schema, bars, cancellationToken).ConfigureAwait(false);
        }

        var info = new FileInfo(path);

        return new DatasetFile(
            IDatasetStore.BarsFileName,
            bars.Count,
            info.Length,
            await HashAsync(path, cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task WriteManifestAsync(
        DatasetManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var directory = Build(manifest.DatasetId, manifest.DatasetVersion);
        System.IO.Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(
                Path.Combine(directory, IDatasetStore.ManifestFileName),
                JsonSerializer.Serialize(manifest, ManifestJson),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DatasetManifest?> ReadManifestAsync(
        string datasetId,
        int version,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Build(datasetId, version), IDatasetStore.ManifestFileName);

        if (!File.Exists(path))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Deserialize<DatasetManifest>(json, ManifestJson);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<int>> ListVersionsAsync(
        string datasetId,
        CancellationToken cancellationToken = default)
    {
        var root = Path.Combine(settings.Directory, datasetId);

        if (!System.IO.Directory.Exists(root))
        {
            return Task.FromResult<IReadOnlyList<int>>([]);
        }

        var versions = System.IO.Directory
            .EnumerateDirectories(root)
            .Select(path => Path.GetFileName(path))
            .Where(name => name.StartsWith('v'))
            .Select(name => int.TryParse(
                name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                ? value
                : -1)
            .Where(value => value > 0)
            .Order()
            .ToList();

        return Task.FromResult<IReadOnlyList<int>>(versions);
    }

    /// <inheritdoc />
    public async Task<string?> ComputeHashAsync(
        string datasetId,
        int version,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        // The name comes out of a manifest, which is a file an operator can
        // edit. Reduced to its last segment so a crafted one cannot walk out of
        // the dataset's own directory.
        var path = Path.Combine(Build(datasetId, version), Path.GetFileName(fileName));

        return File.Exists(path)
            ? await HashAsync(path, cancellationToken).ConfigureAwait(false)
            : null;
    }

    /// <inheritdoc />
    public string NoteFor(string sourceCode) =>
        sourceCode is not null
        && settings.LicenceNotes.TryGetValue(sourceCode, out var note)
        && !string.IsNullOrWhiteSpace(note)
            ? note
            : IDatasetLicenceRegistry.UnstatedNote;

    /// <summary>
    /// The column layout of a dataset's bars.
    /// </summary>
    /// <remarks>
    /// Named in snake_case because the readers are pandas and DuckDB, where it
    /// is the convention, and a column a researcher has to quote to select is a
    /// column they will get wrong once.
    /// </remarks>
    private static ParquetSchema BarSchema() =>
        new(
            new DataField<string>("instrument_id"),
            new DataField<DateTime>("opened_at_utc"),
            new DataField<string>("open"),
            new DataField<string>("high"),
            new DataField<string>("low"),
            new DataField<string>("close"),
            new DataField<long>("volume"),
            new DataField<string?>("turnover"),
            new DataField<string>("source"),
            new DataField<int>("revision"),
            new DataField<string>("price_factor"),
            new DataField<string>("share_factor"));

    /// <summary>
    /// Writes the columns in schema order.
    /// </summary>
    /// <remarks>
    /// Order is not a convenience here: a row group's columns must arrive in
    /// the order the schema declares them, so a column added to the schema and
    /// not to this method produces a file whose values are shifted by one
    /// column and still reads without error.
    /// </remarks>
    private static async Task WriteColumnsAsync(
        ParquetRowGroupWriter group,
        ParquetSchema schema,
        IReadOnlyList<DatasetBar> bars,
        CancellationToken cancellationToken)
    {
        var fields = schema.DataFields;

        await group.WriteAsync(fields[0], bars.Select(bar => bar.InstrumentId.ToString()).ToArray()).ConfigureAwait(false);
        await group.WriteAsync<DateTime>(fields[1], bars.Select(bar => bar.OpenedAtUtc.UtcDateTime).ToArray().AsMemory(), cancellationToken: cancellationToken).ConfigureAwait(false);
        await group.WriteAsync(fields[2], bars.Select(bar => Number(bar.Open)).ToArray()).ConfigureAwait(false);
        await group.WriteAsync(fields[3], bars.Select(bar => Number(bar.High)).ToArray()).ConfigureAwait(false);
        await group.WriteAsync(fields[4], bars.Select(bar => Number(bar.Low)).ToArray()).ConfigureAwait(false);
        await group.WriteAsync(fields[5], bars.Select(bar => Number(bar.Close)).ToArray()).ConfigureAwait(false);
        await group.WriteAsync<long>(fields[6], bars.Select(bar => bar.Volume).ToArray().AsMemory(), cancellationToken: cancellationToken).ConfigureAwait(false);
        await group.WriteAsync(fields[7], bars.Select(bar => bar.Turnover is { } value ? Number(value) : null!).ToArray()).ConfigureAwait(false);
        await group.WriteAsync(fields[8], bars.Select(bar => bar.Source).ToArray()).ConfigureAwait(false);
        await group.WriteAsync<int>(fields[9], bars.Select(bar => bar.Revision).ToArray().AsMemory(), cancellationToken: cancellationToken).ConfigureAwait(false);
        await group.WriteAsync(fields[10], bars.Select(bar => Number(bar.PriceFactor)).ToArray()).ConfigureAwait(false);
        await group.WriteAsync(fields[11], bars.Select(bar => Number(bar.ShareFactor)).ToArray()).ConfigureAwait(false);
    }

    private static string Number(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private string Build(string datasetId, int version) =>
        Path.Combine(
            settings.Directory,
            Path.GetFileName(datasetId),
            string.Create(CultureInfo.InvariantCulture, $"v{version}"));

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);

        return Convert.ToHexStringLower(
            await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
    }
}
