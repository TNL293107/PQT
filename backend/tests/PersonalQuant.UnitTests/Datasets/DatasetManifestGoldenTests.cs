using System.Text.Json;
using PersonalQuant.Application.Datasets;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;
using PersonalQuant.Infrastructure.Datasets;

namespace PersonalQuant.UnitTests.Datasets;

/// <summary>
/// Holds the content hash to the manifests the Python reader is tested
/// against, so the two languages agree on one set of files.
/// </summary>
/// <remarks>
/// <para>
/// The content hash is computed twice, here and in
/// <c>quant/src/personal_quant/research/manifest.py</c>, and a dataset loads
/// only when the two agree. Each side testing its own rendering would leave
/// them free to drift together on one side of the boundary; reading the same
/// committed files from both is what pins them to each other.
/// </para>
/// <para>
/// The version 1 and 2 fixtures are manifests real exports wrote, with their
/// hashes as recorded then. Recomputing them here is what "byte-for-byte as
/// before" means for verification: a change to the rendering that moved any of
/// them would fail every dataset already on disk.
/// </para>
/// <para>
/// The version 3 fixture was written by this test's version 3 build and is
/// now held like the two before it, by its hash. The version 4 fixture is
/// built below and written by this test when <c>PQT_WRITE_GOLDEN=1</c>;
/// otherwise the test asserts the committed file is exactly what this build
/// would write. Neither carries prices, so committing them redistributes
/// nothing.
/// </para>
/// </remarks>
public sealed class DatasetManifestGoldenTests
{
    private const string GoldenV3 = "v3-history.json";

    private const string GoldenV4 = "v4-fundamentals.json";

    [Theory]
    [InlineData("v1-as-of.json")]
    [InlineData("v2-point-in-time.json")]
    [InlineData("v2-known-as-of.json")]
    [InlineData(GoldenV3)]
    [InlineData(GoldenV4)]
    public void A_committed_manifest_hashes_to_the_content_hash_it_records(string name)
    {
        var manifest = Read(name);

        Assert.Equal(manifest.ContentHash, DatasetIdentity.ContentHashOf(manifest));
    }

    [Fact]
    public void The_version_four_fixture_is_exactly_what_this_build_writes()
    {
        var written = JsonSerializer.Serialize(GoldenManifest(), ParquetDatasetStore.ManifestJson)
            .ReplaceLineEndings("\n") + "\n";
        var path = Path.Combine(FixtureDirectory(), GoldenV4);

        if (string.Equals(Environment.GetEnvironmentVariable("PQT_WRITE_GOLDEN"), "1", StringComparison.Ordinal))
        {
            File.WriteAllText(path, written);
        }

        Assert.Equal(written, File.ReadAllText(path).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void The_version_three_fixture_covers_every_shape_the_new_fields_can_take()
    {
        // The fixture is only as good as what it exercises: a hash rendering
        // that mishandled a null would agree with itself on a file that never
        // held one.
        var manifest = Read(GoldenV3);
        var spells = manifest.Instruments.SelectMany(instrument => instrument.Spells ?? []).ToList();

        Assert.Equal(3, manifest.SchemaVersion);
        Assert.True(manifest.HistorySessions > 0);
        Assert.Contains(manifest.Instruments, instrument => instrument.HistoryFrom is not null);
        Assert.Contains(manifest.Instruments, instrument => instrument.HistoryFrom is null);
        Assert.Contains(spells, spell => spell.RemovalAnnouncedOn is not null);
        Assert.Contains(spells, spell => spell.Until is not null && spell.RemovalAnnouncedOn is null);
        Assert.Contains(spells, spell => spell.AnnouncedOn is null);
    }

    [Fact]
    public void The_version_four_fixture_covers_every_shape_the_fundamentals_object_can_take()
    {
        var manifest = Read(GoldenV4);
        var fundamentals = manifest.Fundamentals!;

        Assert.Equal(4, manifest.SchemaVersion);
        Assert.Equal(2, manifest.Files.Count);
        Assert.Equal(manifest.Files[1].RowCount, fundamentals.Rows);
        Assert.Equal(
            ["ids_capture_gap", "not_imported", "out_of_scope"],
            fundamentals.NotCollected.Select(gap => gap.Reason).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_version_four_manifest_hashes_its_fundamentals_and_a_version_three_one_does_not()
    {
        var manifest = GoldenManifest();
        var fundamentals = manifest.Fundamentals!;

        Assert.NotEqual(
            DatasetIdentity.ContentHashOf(manifest),
            DatasetIdentity.ContentHashOf(manifest with { Fundamentals = null }));
        Assert.NotEqual(
            DatasetIdentity.ContentHashOf(manifest),
            DatasetIdentity.ContentHashOf(manifest with { Fundamentals = fundamentals with { NotCollected = fundamentals.NotCollected.Skip(1).ToList() } }));

        // The licence note records the deployment's terms, not the data.
        Assert.Equal(
            DatasetIdentity.ContentHashOf(manifest),
            DatasetIdentity.ContentHashOf(manifest with { Fundamentals = fundamentals with { LicenceNote = "changed" } }));

        var asVersionThree = Read(GoldenV3);

        Assert.Equal(
            DatasetIdentity.ContentHashOf(asVersionThree),
            DatasetIdentity.ContentHashOf(asVersionThree with { Fundamentals = fundamentals }));
    }

    [Fact]
    public void A_version_three_manifest_hashes_its_history_and_a_version_two_one_does_not()
    {
        // The new fields enter the hash only from version 3, so a version 2
        // manifest read back with their defaults hashes as it always did, and a
        // version 3 one cannot have them edited without failing verification.
        var manifest = Read(GoldenV3);

        Assert.NotEqual(
            DatasetIdentity.ContentHashOf(manifest),
            DatasetIdentity.ContentHashOf(manifest with { HistorySessions = manifest.HistorySessions + 1 }));

        var asVersionTwo = manifest with { SchemaVersion = 2 };

        Assert.Equal(
            DatasetIdentity.ContentHashOf(asVersionTwo),
            DatasetIdentity.ContentHashOf(asVersionTwo with { HistorySessions = 0 }));
    }

    /// <summary>
    /// A point-in-time VN30 manifest with 253 sessions of history and
    /// fundamentals, built from a real request so its identifier is the one
    /// the export would derive. Its instruments are those of the version 3
    /// fixture; two of them are not collected, for two different reasons, and
    /// a third has no rows because it has no bars and no report.
    /// </summary>
    private static DatasetManifest GoldenManifest()
    {
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 9, 17);

        Assert.True(DatasetRequest.TryCreatePointInTime(
            UniverseCode.Create("VN30"),
            BarInterval.OneDay,
            from,
            to,
            out var request,
            out var problem,
            knownAsOfUtc: new DateTimeOffset(2026, 9, 18, 3, 4, 5, 500, TimeSpan.Zero),
            historySessions: 253,
            fundamentals: true),
            problem);

        var manifest = new DatasetManifest(
            request.DatasetId,
            2,
            DatasetManifest.CurrentSchemaVersion,
            string.Empty,
            request.KnownAsOfUtc,
            request.Adjusted,
            request.AnnouncementPolicy,
            request.UniverseCode.Value,
            request.UniverseAsOf,
            [
                // A member throughout, with a full year of history before it.
                new DatasetInstrument(
                    Guid.Parse("0f3c1a52-7d10-4c6e-9a0b-1d2e3f405161"),
                    "AAA",
                    "HOSE",
                    [new DatasetSpell(from, null, new DateOnly(2025, 7, 17))],
                    new DateOnly(2025, 6, 25)),

                // Admitted at the August review; its history runs up to the day
                // before, partly inside the window.
                new DatasetInstrument(
                    Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c5d"),
                    "BBB",
                    "HOSE",
                    [new DatasetSpell(new DateOnly(2026, 8, 3), null, new DateOnly(2026, 7, 15))],
                    new DateOnly(2025, 7, 28)),

                // Removed at the same review, with the removal announced.
                new DatasetInstrument(
                    Guid.Parse("2b3c4d5e-6f70-4b8c-9dae-bf0011223344"),
                    "CCC",
                    "HNX",
                    [new DatasetSpell(from, new DateOnly(2026, 8, 3), new DateOnly(2025, 7, 17), new DateOnly(2026, 7, 15))],
                    new DateOnly(2025, 6, 25)),

                // Out and back: the first removal was never announced, the
                // admission was never announced either.
                new DatasetInstrument(
                    Guid.Parse("3c4d5e6f-7081-4c9d-aebf-c01122334455"),
                    "DDD",
                    "HOSE",
                    [
                        new DatasetSpell(from, new DateOnly(2026, 7, 20), null),
                        new DatasetSpell(new DateOnly(2026, 8, 3), null, new DateOnly(2026, 7, 15)),
                    ],
                    new DateOnly(2025, 6, 25)),

                // A constituent with no stored bars: no rows, so no history.
                new DatasetInstrument(
                    Guid.Parse("4d5e6f70-8192-4dae-bfc0-d12233445566"),
                    "EEE",
                    "UPCOM",
                    [new DatasetSpell(from, null, new DateOnly(2025, 7, 17))],
                    null),
            ],
            BarIntervalParser.Describe(request.Interval),
            request.FromDate,
            request.ToDate,
            1,
            1,
            2,
            [new DatasetSource("CAFEF", IDatasetLicenceRegistry.UnstatedNote)],
            [
                new DatasetFile(IDatasetStore.BarsFileName, 1_265, 41_000, new string('5', 64)),
                new DatasetFile(IDatasetStore.FundamentalsFileName, 160, 23_000, new string('6', 64)),
            ],
            new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.Zero),
            null,
            request.Membership,
            request.HistorySessions,
            new DatasetFundamentals(
                1,
                "IDS",
                IDatasetLicenceRegistry.UnstatedNote,
                DatasetFundamentals.SessionCut,
                160,
                [
                    new DatasetNotCollected(Guid.Parse("2b3c4d5e-6f70-4b8c-9dae-bf0011223344"), "ids_capture_gap"),
                    new DatasetNotCollected(Guid.Parse("3c4d5e6f-7081-4c9d-aebf-c01122334455"), "out_of_scope"),
                    new DatasetNotCollected(Guid.Parse("4d5e6f70-8192-4dae-bfc0-d12233445566"), "not_imported"),
                ]));

        return manifest with { ContentHash = DatasetIdentity.ContentHashOf(manifest) };
    }

    private static DatasetManifest Read(string name) =>
        JsonSerializer.Deserialize<DatasetManifest>(
            File.ReadAllText(Path.Combine(FixtureDirectory(), name)),
            ParquetDatasetStore.ManifestJson)!;

    /// <summary>Walks up from the test binary to the Python side's fixtures.</summary>
    private static string FixtureDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "quant", "tests", "fixtures", "manifests");

            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "quant/tests/fixtures/manifests was not found above the test binary.");
    }
}
