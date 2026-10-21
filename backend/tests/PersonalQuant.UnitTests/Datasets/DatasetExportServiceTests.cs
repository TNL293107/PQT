using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Parquet;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Datasets;
using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Application.Universes;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;
using PersonalQuant.Infrastructure.Datasets;
using PersonalQuant.UnitTests.CorporateActions.Fakes;
using PersonalQuant.UnitTests.Fundamentals;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.Datasets;

/// <summary>
/// Verifies the export end to end, over the real Parquet store.
/// </summary>
/// <remarks>
/// The store is not faked. Everything U5 promises — a file on disk, a hash that
/// matches it, a manifest that can be read back — is a property of the bytes
/// actually written, and a fake writer would verify none of it. The directory
/// is a temporary one and is removed afterwards.
/// </remarks>
public sealed class DatasetExportServiceTests : IDisposable
{
    private static readonly DateOnly From = new(2026, 8, 3);
    private static readonly DateOnly To = new(2026, 8, 7);
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 1, 0, 0, TimeSpan.Zero);
    private static readonly UniverseCode Vn30 = UniverseCode.Create("VN30");

    private readonly string root = Path.Combine(
        Path.GetTempPath(), "pqt-datasets", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task An_export_writes_a_file_a_manifest_and_a_hash_that_matches()
    {
        var harness = new Harness(root);

        var export = await harness.ExportAsync();

        Assert.True(export.Succeeded);

        var manifest = export.Manifest!;
        var file = Assert.Single(manifest.Files);

        Assert.Equal(IDatasetStore.BarsFileName, file.Name);
        Assert.Equal(5, file.RowCount);
        Assert.True(File.Exists(Path.Combine(root, manifest.DatasetId, "v1", file.Name)));

        var verification = await harness.VerifyAsync(manifest.DatasetId, 1);

        Assert.True(verification.IsIntact);
        Assert.Equal(1, verification.FilesChecked);
    }

    [Fact]
    public async Task A_changed_file_fails_verification()
    {
        // The whole point of recording a digest. A dataset whose bytes no longer
        // match its manifest must be a hard error, because a reproducible result
        // computed from a silently changed input is worse than no result.
        var harness = new Harness(root);
        var manifest = (await harness.ExportAsync()).Manifest!;

        await File.WriteAllTextAsync(
            Path.Combine(root, manifest.DatasetId, "v1", IDatasetStore.BarsFileName),
            "not a parquet file",
            TestContext.Current.CancellationToken);

        var verification = await harness.VerifyAsync(manifest.DatasetId, 1);

        Assert.False(verification.IsIntact);
        Assert.Contains(verification.Problems, problem => problem.Contains("hashes to", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_edited_manifest_fails_verification_even_with_the_data_untouched()
    {
        // A manifest is as editable as the data it describes. Checking only the
        // files would let somebody change the as-of a dataset claims, or the
        // policy it was built under, and leave every hash matching.
        var harness = new Harness(root);
        var manifest = (await harness.ExportAsync()).Manifest!;
        var path = Path.Combine(root, manifest.DatasetId, "v1", IDatasetStore.ManifestFileName);

        var json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(
            path,
            json.Replace("\"strict\"", "\"permissive\"", StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        var verification = await harness.VerifyAsync(manifest.DatasetId, 1);

        Assert.False(verification.IsIntact);
        Assert.Contains(
            verification.Problems,
            problem => problem.Contains("fields have been changed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_second_export_of_the_same_parameters_is_a_new_version_of_the_same_dataset()
    {
        var harness = new Harness(root);

        var one = (await harness.ExportAsync()).Manifest!;
        var two = (await harness.ExportAsync()).Manifest!;

        Assert.Equal(one.DatasetId, two.DatasetId);
        Assert.Equal(1, one.DatasetVersion);
        Assert.Equal(2, two.DatasetVersion);

        // The reproducibility property. The version says which run this was;
        // the content hash says what the data is, and rebuilding identical
        // parameters over identical data must not change it.
        Assert.Equal(one.ContentHash, two.ContentHash);
    }

    [Fact]
    public async Task An_unknown_membership_refuses_rather_than_exporting_an_empty_market()
    {
        // The refusal U2 exists to make possible. An empty dataset would produce
        // a backtest that reports no positions and no error.
        var harness = new Harness(root, membershipKnown: false);

        var export = await harness.ExportAsync();

        Assert.False(export.Succeeded);
        Assert.Contains("is not known", export.Problem, StringComparison.Ordinal);
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task A_universe_with_no_stored_bars_refuses_rather_than_writing_nothing()
    {
        var harness = new Harness(root, storeBars: false);

        var export = await harness.ExportAsync();

        Assert.False(export.Succeeded);
        Assert.Contains("empty dataset", export.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_source_nobody_has_stated_terms_for_is_recorded_as_unlicensed()
    {
        // The true position for every Vietnamese source this system reads. A
        // dataset that left with no statement attached would be one somebody
        // eventually redistributes.
        var harness = new Harness(root);

        var manifest = (await harness.ExportAsync()).Manifest!;
        var source = Assert.Single(manifest.Sources);

        Assert.Equal("TEST", source.Code);
        Assert.Equal(IDatasetLicenceRegistry.UnstatedNote, source.LicenceNote);
    }

    [Fact]
    public async Task The_manifest_carries_the_reading_the_bars_were_taken_under()
    {
        var harness = new Harness(root);

        var manifest = (await harness.ExportAsync()).Manifest!;

        Assert.Equal(Domain.CorporateActions.AnnouncementPolicy.Strict, manifest.AnnouncementPolicy);
        Assert.True(manifest.Adjusted);
        Assert.Equal(DataRules.TransformationVersion, manifest.TransformationVersion);
        Assert.Equal(DataRules.ValidationVersion, manifest.ValidationVersion);
        Assert.Equal(DataRules.AdjustmentVersion, manifest.AdjustmentVersion);
        Assert.Equal(DatasetManifest.CurrentSchemaVersion, manifest.SchemaVersion);

        // The label lives here, stamped with the instant it was valid, and
        // nowhere in the rows.
        var instrument = Assert.Single(manifest.Instruments);
        Assert.Equal("EXP", instrument.TickerAtAsOf);
    }

    [Fact]
    public async Task The_written_file_reads_back_as_parquet_with_the_columns_the_contract_names()
    {
        // Hashing bytes proves they have not changed; it does not prove they
        // are a Parquet file, nor that the columns arrived in the order the
        // schema declares. A row group written out of order still reads
        // without error, with every value one column to the left.
        var harness = new Harness(root);
        var manifest = (await harness.ExportAsync()).Manifest!;
        var path = Path.Combine(root, manifest.DatasetId, "v1", IDatasetStore.BarsFileName);

        await using var stream = File.OpenRead(path);
        await using var reader = await ParquetReader.CreateAsync(
            stream, cancellationToken: TestContext.Current.CancellationToken);

        var fields = reader.Schema.DataFields;

        Assert.Equal(
            [
                "instrument_id",
                "opened_at_utc",
                "open",
                "high",
                "low",
                "close",
                "volume",
                "turnover",
                "source",
                "revision",
                "price_factor",
                "share_factor",
            ],
            fields.Select(field => field.Name));

        using var group = reader.OpenRowGroupReader(0);

        Assert.Equal(5, group.RowCount);

        var close = new string?[group.RowCount];
        await group.ReadAsync(
            fields[5], close.AsMemory(), cancellationToken: TestContext.Current.CancellationToken);

        // Prices are text, exactly as stored. A decimal forced through binary
        // floating point stops being the number the market printed.
        Assert.All(close, value => Assert.Equal("100", value));

        var open = new string?[group.RowCount];
        await group.ReadAsync(
            fields[2], open.AsMemory(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(open, value => Assert.Equal("100.5", value));

        // The first bar's turnover was absent and the rest were not, so the
        // column has to carry the absence rather than a zero.
        var turnover = new string?[group.RowCount];
        await group.ReadAsync(
            fields[7], turnover.AsMemory(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(turnover[0]);
        Assert.Equal("100000", turnover[1]);
    }

    /// <summary>Wires the real export over in-memory reads and a temp directory.</summary>
    [Fact]
    public async Task A_point_in_time_export_keeps_only_the_sessions_the_instrument_belonged_on()
    {
        // Member Monday to Wednesday, removed at Thursday's review. An as-of
        // export read on Monday would carry Thursday and Friday too - prices
        // from after it left, attributed to an index it was no longer in.
        var harness = new Harness(root, spells: (id, _) => [Spell(id, From, new DateOnly(2026, 8, 6), new DateOnly(2026, 7, 20))]);

        var export = await harness.ExportAsync(pointInTime: true);

        Assert.True(export.Succeeded, export.Problem);

        var manifest = export.Manifest!;
        Assert.Equal(DatasetMembership.PointInTime, manifest.Membership);
        Assert.Null(manifest.UniverseAsOf);
        Assert.Equal(3, Assert.Single(manifest.Files).RowCount);

        var spell = Assert.Single(Assert.Single(manifest.Instruments).Spells!);
        Assert.Equal(new DatasetSpell(From, new DateOnly(2026, 8, 6), new DateOnly(2026, 7, 20)), spell);
    }

    [Fact]
    public async Task A_spell_that_began_before_the_window_and_is_still_open_is_clipped_to_it()
    {
        var harness = new Harness(root, spells: (id, _) => [Spell(id, new DateOnly(2025, 8, 4), null, null)]);

        var export = await harness.ExportAsync(pointInTime: true);

        var manifest = export.Manifest!;
        Assert.Equal(5, Assert.Single(manifest.Files).RowCount);
        Assert.Equal(new DatasetSpell(From, null, null), Assert.Single(Assert.Single(manifest.Instruments).Spells!));
    }

    [Fact]
    public async Task A_security_that_left_and_returned_contributes_only_its_two_spells()
    {
        // Out after Monday, back from Thursday. The gap is exactly what a
        // survivorship-free backtest must be able to see, so no bar from it may
        // reach the file.
        var harness = new Harness(root, spells: (id, _) =>
        [
            Spell(id, From, new DateOnly(2026, 8, 4), null),
            Spell(id, new DateOnly(2026, 8, 6), null, null),
        ]);

        var export = await harness.ExportAsync(pointInTime: true);

        Assert.Equal(3, Assert.Single(export.Manifest!.Files).RowCount);
        Assert.Equal(2, Assert.Single(export.Manifest!.Instruments).Spells!.Count);
    }

    [Fact]
    public async Task A_review_inside_the_window_hands_the_sessions_from_one_name_to_the_other()
    {
        // The case point-in-time exists for. One security leaves at Thursday's
        // review and another joins on the same date: the file holds the leaver
        // Monday to Wednesday and the joiner Thursday and Friday, never both on
        // one session, and never either outside its spell.
        var harness = new Harness(root, spells: (leaver, joiner) =>
        [
            Spell(leaver, new DateOnly(2025, 8, 4), new DateOnly(2026, 8, 6), null),
            Spell(joiner, new DateOnly(2026, 8, 6), null, new DateOnly(2026, 7, 15)),
        ]);

        var export = await harness.ExportAsync(pointInTime: true);

        var manifest = export.Manifest!;
        Assert.Equal(5, Assert.Single(manifest.Files).RowCount);
        Assert.Equal(2, manifest.Instruments.Count);

        // Ordered by identifier, so two exports of the same data hash alike.
        Assert.Equal(
            manifest.Instruments.Select(instrument => instrument.InstrumentId).Order(),
            manifest.Instruments.Select(instrument => instrument.InstrumentId));

        var joined = manifest.Instruments.Single(instrument => instrument.InstrumentId == harness.Joiner.Value);
        Assert.Equal(new DatasetSpell(new DateOnly(2026, 8, 6), null, new DateOnly(2026, 7, 15)), Assert.Single(joined.Spells!));
    }

    [Fact]
    public async Task A_window_whose_membership_is_not_known_on_every_session_refuses()
    {
        var harness = new Harness(root, membershipKnown: false);

        var export = await harness.ExportAsync(pointInTime: true);

        Assert.False(export.Succeeded);
        Assert.Contains("every session", export.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_point_in_time_dataset_verifies_and_an_edited_spell_does_not()
    {
        var harness = new Harness(root, spells: (id, _) => [Spell(id, From, new DateOnly(2026, 8, 6), null)]);
        var manifest = (await harness.ExportAsync(pointInTime: true)).Manifest!;

        Assert.True((await harness.VerifyAsync(manifest.DatasetId, 1)).IsIntact);

        // The spells are part of what the data is: the same rows under a
        // longer spell claim a different membership.
        var path = Path.Combine(root, manifest.DatasetId, "v1", IDatasetStore.ManifestFileName);
        await File.WriteAllTextAsync(
            path,
            (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken))
                .Replace("\"until\": \"2026-08-06\"", "\"until\": \"2026-08-07\"", StringComparison.Ordinal),
            TestContext.Current.CancellationToken);

        Assert.False((await harness.VerifyAsync(manifest.DatasetId, 1)).IsIntact);
    }

    [Theory]
    [InlineData(false, "\"membership\": \"as_of\"", "\"membership\": \"point_in_time\"")]
    [InlineData(true, "\"membership\": \"point_in_time\"", "\"membership\": \"as_of\"")]
    public async Task A_manifest_whose_membership_was_edited_fails_verification(
        bool pointInTime,
        string written,
        string forged)
    {
        // The mode is the claim a consumer trusts most: point_in_time says the
        // rows are survivorship-free. Flipping it alone - leaving the as-of,
        // the spells and the hash untouched - must not verify.
        var harness = new Harness(root);
        var manifest = (await harness.ExportAsync(pointInTime)).Manifest!;
        var path = Path.Combine(root, manifest.DatasetId, "v1", IDatasetStore.ManifestFileName);
        var json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        Assert.Contains(written, json, StringComparison.Ordinal);
        await File.WriteAllTextAsync(
            path, json.Replace(written, forged, StringComparison.Ordinal), TestContext.Current.CancellationToken);

        Assert.False((await harness.VerifyAsync(manifest.DatasetId, 1)).IsIntact);
    }

    [Fact]
    public async Task Without_history_no_row_falls_outside_a_spell_and_none_is_claimed()
    {
        var harness = new Harness(
            root,
            spells: (id, _) => [Spell(id, new DateOnly(2026, 8, 5), null, null)],
            seriesFrom: new DateOnly(2026, 7, 27));

        var manifest = (await harness.ExportAsync(pointInTime: true)).Manifest!;

        Assert.Equal(0, manifest.HistorySessions);
        Assert.Null(Assert.Single(manifest.Instruments).HistoryFrom);
        Assert.Equal(3, Assert.Single(manifest.Files).RowCount);
    }

    [Fact]
    public async Task A_name_admitted_mid_window_carries_the_bars_before_its_admission()
    {
        // The case history exists for. Admitted on Wednesday 5 August, so a
        // point-in-time export holds three of its bars, and a factor needing
        // more than three sessions cannot score it until it has belonged for
        // that long. With history the four bars before admission - two inside
        // the window, two before it - are written as data, while its spell
        // still says it joined on Wednesday.
        var harness = new Harness(
            root,
            spells: (id, _) => [Spell(id, new DateOnly(2026, 8, 5), null, new DateOnly(2026, 7, 15))],
            seriesFrom: new DateOnly(2026, 7, 27));

        var export = await harness.ExportAsync(pointInTime: true, history: 4);

        Assert.True(export.Succeeded, export.Problem);

        var manifest = export.Manifest!;
        var instrument = Assert.Single(manifest.Instruments);

        Assert.Equal(DatasetManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.Equal(4, manifest.HistorySessions);
        Assert.Equal(new DateOnly(2026, 7, 30), instrument.HistoryFrom);
        Assert.Equal(7, Assert.Single(manifest.Files).RowCount);
        Assert.Equal(
            new DatasetSpell(new DateOnly(2026, 8, 5), null, new DateOnly(2026, 7, 15)),
            Assert.Single(instrument.Spells!));
        Assert.Equal(
            [
                new DateOnly(2026, 7, 30), new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 3),
                new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 6),
                new DateOnly(2026, 8, 7),
            ],
            await harness.SessionsAsync(manifest));
    }

    [Fact]
    public async Task History_fills_the_gap_between_two_spells_so_the_series_is_continuous()
    {
        // Out after Monday, back from Thursday. Without history the file has a
        // two-session hole a lookback would read as missing data; with it the
        // hole is filled, and the spells still say the name was not a member.
        var harness = new Harness(
            root,
            spells: (id, _) =>
            [
                Spell(id, new DateOnly(2025, 8, 4), new DateOnly(2026, 8, 4), null, new DateOnly(2026, 7, 20)),
                Spell(id, new DateOnly(2026, 8, 6), null, null),
            ],
            seriesFrom: new DateOnly(2026, 7, 27));

        var manifest = (await harness.ExportAsync(pointInTime: true, history: 2)).Manifest!;
        var instrument = Assert.Single(manifest.Instruments);

        Assert.Equal(new DateOnly(2026, 7, 30), instrument.HistoryFrom);
        Assert.Equal(7, Assert.Single(manifest.Files).RowCount);
        Assert.Equal(2, instrument.Spells!.Count);
        Assert.Equal(new DateOnly(2026, 7, 20), instrument.Spells[0].RemovalAnnouncedOn);
        Assert.Null(instrument.Spells[1].RemovalAnnouncedOn);
    }

    [Fact]
    public async Task No_row_is_written_after_the_last_spell_ends()
    {
        // History looks backwards only. A bar after the name left would be a
        // price from outside the universe that nothing can use as history.
        var harness = new Harness(
            root,
            spells: (id, _) => [Spell(id, new DateOnly(2025, 8, 4), new DateOnly(2026, 8, 6), null)],
            seriesFrom: new DateOnly(2026, 7, 27));

        var manifest = (await harness.ExportAsync(pointInTime: true, history: 2)).Manifest!;

        Assert.Equal(
            [
                new DateOnly(2026, 7, 30), new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 3),
                new DateOnly(2026, 8, 4), new DateOnly(2026, 8, 5),
            ],
            await harness.SessionsAsync(manifest));
    }

    [Fact]
    public async Task History_reaches_back_only_as_far_as_the_series_goes()
    {
        var harness = new Harness(
            root,
            spells: (id, _) => [Spell(id, new DateOnly(2026, 8, 5), null, null)],
            seriesFrom: new DateOnly(2026, 7, 29));

        var manifest = (await harness.ExportAsync(pointInTime: true, history: DatasetRequest.MaxHistorySessions)).Manifest!;

        Assert.Equal(new DateOnly(2026, 7, 29), Assert.Single(manifest.Instruments).HistoryFrom);
        Assert.Equal(8, Assert.Single(manifest.Files).RowCount);
    }

    [Fact]
    public async Task An_as_of_export_with_history_reaches_before_the_window()
    {
        var harness = new Harness(root, seriesFrom: new DateOnly(2026, 7, 27));

        var manifest = (await harness.ExportAsync(history: 2)).Manifest!;
        var instrument = Assert.Single(manifest.Instruments);

        Assert.Null(instrument.Spells);
        Assert.Equal(new DateOnly(2026, 7, 30), instrument.HistoryFrom);
        Assert.Equal(7, Assert.Single(manifest.Files).RowCount);
    }

    [Fact]
    public async Task A_removal_announcement_is_carried_only_for_what_the_window_could_know()
    {
        // A removal after the window keeps its announcement only when that was
        // published within the window: a later date is not something this
        // dataset describes, and carrying it would tell a strategy in August
        // what the index decided in September.
        var harness = new Harness(root, spells: (leaver, joiner) =>
        [
            Spell(leaver, From, new DateOnly(2026, 8, 10), null, new DateOnly(2026, 8, 5)),
            Spell(joiner, From, new DateOnly(2026, 9, 1), null, new DateOnly(2026, 8, 20)),
        ]);

        var manifest = (await harness.ExportAsync(pointInTime: true)).Manifest!;

        var announced = manifest.Instruments.Single(instrument => instrument.InstrumentId == harness.InstrumentId.Value);
        var later = manifest.Instruments.Single(instrument => instrument.InstrumentId == harness.Joiner.Value);

        Assert.Equal(new DatasetSpell(From, null, null, new DateOnly(2026, 8, 5)), Assert.Single(announced.Spells!));
        Assert.Equal(new DatasetSpell(From, null, null, null), Assert.Single(later.Spells!));
    }

    [Theory]
    [InlineData("\"history_sessions\": 2", "\"history_sessions\": 3")]
    [InlineData("\"history_from\": \"2026-08-03\"", "\"history_from\": \"2026-07-31\"")]
    public async Task A_history_dataset_verifies_and_an_edited_history_claim_does_not(string written, string forged)
    {
        // Both claims decide which rows a reader accepts as history, so either
        // edited alone - widening what may sit outside a spell - must fail.
        var harness = new Harness(
            root,
            spells: (id, _) => [Spell(id, new DateOnly(2026, 8, 5), null, null)],
            seriesFrom: new DateOnly(2026, 7, 27));
        var manifest = (await harness.ExportAsync(pointInTime: true, history: 2)).Manifest!;

        Assert.True((await harness.VerifyAsync(manifest.DatasetId, 1)).IsIntact);

        var path = Path.Combine(root, manifest.DatasetId, "v1", IDatasetStore.ManifestFileName);
        var json = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        Assert.Contains(written, json, StringComparison.Ordinal);
        await File.WriteAllTextAsync(
            path, json.Replace(written, forged, StringComparison.Ordinal), TestContext.Current.CancellationToken);

        Assert.False((await harness.VerifyAsync(manifest.DatasetId, 1)).IsIntact);
    }

    [Fact]
    public async Task A_history_export_is_a_different_dataset_from_the_same_window_without_it()
    {
        var harness = new Harness(root, seriesFrom: new DateOnly(2026, 7, 27));

        var without = (await harness.ExportAsync(pointInTime: true)).Manifest!;
        var with = (await harness.ExportAsync(pointInTime: true, history: 2)).Manifest!;

        Assert.NotEqual(without.DatasetId, with.DatasetId);
        Assert.Equal(1, with.DatasetVersion);
    }

    [Fact]
    public void An_as_of_request_keeps_the_identifier_it_had_before_point_in_time_existed()
    {
        // Datasets already built are named by these identifiers. Changing how
        // an as-of request is named would orphan every one of them.
        Assert.True(DatasetRequest.TryCreate(
            Vn30, To, BarInterval.OneDay, From, To, out var request, out var problem), problem);

        var canonical = "VN30|2026-08-07|OneDay|2026-08-03|2026-08-07|adjusted|current|Strict";
        var expected = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)))[..16];

        Assert.Equal(expected, request.DatasetId);
    }

    [Fact]
    public void A_point_in_time_request_names_a_different_dataset_from_any_as_of_one()
    {
        Assert.True(DatasetRequest.TryCreatePointInTime(
            Vn30, BarInterval.OneDay, From, To, out var pointInTime, out var problem), problem);
        Assert.True(DatasetRequest.TryCreate(
            Vn30, To, BarInterval.OneDay, From, To, out var asOf, out problem), problem);

        Assert.NotEqual(asOf.DatasetId, pointInTime.DatasetId);
        Assert.Null(pointInTime.UniverseAsOf);
        Assert.Equal(DatasetMembership.PointInTime, pointInTime.Membership);
    }

    [Fact]
    public async Task An_export_without_fundamentals_writes_a_version_4_manifest_with_none()
    {
        var manifest = (await new Harness(root).ExportAsync()).Manifest!;

        Assert.Equal(DatasetManifest.FundamentalsSchemaVersion, manifest.SchemaVersion);
        Assert.Null(manifest.Fundamentals);
        Assert.Equal([IDatasetStore.BarsFileName], manifest.Files.Select(file => file.Name));
    }

    [Fact]
    public void A_request_with_fundamentals_names_its_own_dataset_and_keeps_the_others_names()
    {
        Assert.True(DatasetRequest.TryCreate(
            Vn30, To, BarInterval.OneDay, From, To, out var request, out var problem, fundamentals: true), problem);

        var canonical = "VN30|2026-08-07|OneDay|2026-08-03|2026-08-07|adjusted|current|Strict|fundamentals";
        var expected = Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical)))[..16];

        Assert.Equal(expected, request.DatasetId);
    }

    [Fact]
    public async Task An_export_with_fundamentals_writes_one_row_per_membership_session_and_verifies()
    {
        var harness = new Harness(root);
        var report = harness.FileReport(harness.InstrumentId);

        var manifest = (await harness.ExportAsync(pointInTime: true, fundamentals: true)).Manifest!;

        Assert.Equal([IDatasetStore.BarsFileName, IDatasetStore.FundamentalsFileName], manifest.Files.Select(file => file.Name));
        Assert.Equal(5, manifest.Files[1].RowCount);

        var fundamentals = manifest.Fundamentals!;
        Assert.Equal((FundamentalRatioService.RatioVersion, "IDS", "session_date_00utc", 5L), (fundamentals.RatioVersion, fundamentals.Source, fundamentals.Cut, fundamentals.Rows));
        Assert.Empty(fundamentals.NotCollected);
        Assert.True((await harness.VerifyAsync(manifest.DatasetId, 1)).IsIntact);

        var columns = await harness.ColumnsAsync(manifest, IDatasetStore.FundamentalsFileName);

        Assert.Equal(10 + (2 * DatasetFundamentalsRow.ValueNames.Count) + 1, columns.Count);
        Assert.All(columns["scope"], scope => Assert.Equal("consolidated", scope));
        Assert.All(columns["balance_sheet_quarter"], quarter => Assert.Equal("2026Q2", quarter));
        Assert.All(columns["report_ids"], ids => Assert.Equal($"[\"{report.Id.Value}\"]", ids));
        Assert.All(columns["shares"], shares => Assert.Equal("100000000", shares));
        Assert.All(columns["shares_absent"], Assert.Null);

        // No close is recorded in the bar store, and one quarter of profit is
        // not a trailing year: absent, with the codes, never zero.
        Assert.All(columns["close_raw"], Assert.Null);
        Assert.All(columns["close_raw_absent"], code => Assert.Equal("no_close", code));
        Assert.All(columns["eps_ttm_absent"], code => Assert.Equal("missing_quarter", code));
        Assert.All(columns["findings"], findings => Assert.StartsWith("[\"No corporate action is recorded", findings));
    }

    [Fact]
    public async Task A_name_with_no_report_has_no_rows_and_is_listed_as_not_collected()
    {
        var harness = new Harness(root);

        var manifest = (await harness.ExportAsync(pointInTime: true, fundamentals: true)).Manifest!;

        var gap = Assert.Single(manifest.Fundamentals!.NotCollected);
        Assert.Equal((harness.InstrumentId.Value, "not_imported"), (gap.InstrumentId, gap.Reason));
        Assert.Equal(0, manifest.Fundamentals.Rows);
        Assert.Equal("none", manifest.Fundamentals.Source);
    }

    [Fact]
    public async Task A_report_recorded_after_the_observation_instant_is_not_in_the_dataset()
    {
        var harness = new Harness(root);
        harness.FileReport(harness.InstrumentId);

        // The report was recorded on 31 July; a dataset observed the day
        // before holds nothing of it.
        var manifest = (await harness.ExportAsync(
            pointInTime: true, fundamentals: true, knownAsOf: new DateTimeOffset(2026, 7, 30, 0, 0, 0, TimeSpan.Zero))).Manifest;

        Assert.NotNull(manifest);
        Assert.Equal("not_imported", Assert.Single(manifest.Fundamentals!.NotCollected).Reason);
    }

    [Fact]
    public async Task Changing_the_fundamentals_object_breaks_the_content_hash()
    {
        var harness = new Harness(root);
        harness.FileReport(harness.InstrumentId);
        var manifest = (await harness.ExportAsync(pointInTime: true, fundamentals: true)).Manifest!;

        var edited = manifest with { Fundamentals = manifest.Fundamentals! with { RatioVersion = manifest.Fundamentals.RatioVersion + 1 } };

        Assert.Equal(manifest.ContentHash, DatasetIdentity.ContentHashOf(manifest));
        Assert.NotEqual(manifest.ContentHash, DatasetIdentity.ContentHashOf(edited));
    }

    private static UniverseSpell Spell(
        InstrumentId id,
        DateOnly from,
        DateOnly? until,
        DateOnly? announcedOn,
        DateOnly? removalAnnouncedOn = null) =>
        new(id, from, until, announcedOn, removalAnnouncedOn);

    private sealed class Harness
    {
        private readonly DatasetExportService service;

        public Harness(
            string root,
            bool membershipKnown = true,
            bool storeBars = true,
            Func<InstrumentId, InstrumentId, IReadOnlyList<UniverseSpell>>? spells = null,
            DateOnly? seriesFrom = null)
        {
            InstrumentId = InstrumentId.New();
            Root = root;

            var store = new ParquetDatasetStore(
                Options.Create(new DatasetOptions { Directory = root }));

            service = new DatasetExportService(
                new FakeUniverseCatalog(
                    membershipKnown ? [InstrumentId] : null,
                    membershipKnown ? spells?.Invoke(InstrumentId, Joiner) ?? [Spell(InstrumentId, From, null, null)] : null),
                new FakeInstrumentDetails(InstrumentId, Joiner),
                new FakeSeries(storeBars, seriesFrom ?? From),
                store,
                store,
                new FundamentalsReader(Fundamentals),
                new FundamentalRatioService(new FundamentalsReader(Fundamentals), Bars, new FakeCorporateActionRepository(), new FakeCorporateActionCoverageRepository()),
                new FakeBuild(),
                new FakeClock(Now),
                NullLogger<DatasetExportService>.Instance);
        }

        public InstrumentId InstrumentId { get; }

        public string Root { get; }

        /// <summary>The statements the ratios are read from; empty unless a test files some.</summary>
        public InMemoryFundamentals Fundamentals { get; } = new();

        /// <summary>The closes the ratios are priced at; empty unless a test records some.</summary>
        public FakeBarRepository Bars { get; } = new();

        /// <summary>
        /// Files a quarterly report for 2026Q2 on the Circular 200 form:
        /// ₫1,000bn of capital, ₫10,000bn of equity and ₫250bn of profit to
        /// the parent, disclosed on 30 July 2026.
        /// </summary>
        public FinancialReport FileReport(InstrumentId instrument)
        {
            var recorded = new DateTimeOffset(2026, 7, 31, 0, 0, 0, TimeSpan.Zero);
            var quarter = FiscalQuarter.Create(2026, 2);
            var report = FinancialReport.Record(
                instrument, SourceCode.Create("IDS"), $"ids-{Guid.NewGuid():N}", FinancialReportKind.Quarterly,
                FinancialStatementScope.Consolidated, FinancialTemplate.NonFinancialCircular200, quarter,
                "Báo cáo tài chính", new DateOnly(2026, 7, 30), null, FinancialReportTests.Vietnam,
                isCorrection: false, valuesFiled: true, recorded);
            Fundamentals.Add(report);

            foreach (var (statement, code, span, value) in new[]
            {
                (FinancialStatement.BalanceSheet, "411a", PeriodSpan.Instant, 1_000_000_000_000m),
                (FinancialStatement.BalanceSheet, "400", PeriodSpan.Instant, 10_000_000_000_000m),
                (FinancialStatement.IncomeStatement, "61", PeriodSpan.Quarter, 250_000_000_000m),
            })
            {
                Fundamentals.Add(StatementLine.Observe(
                    report, statement, code, quarter, span, value, ValueBasis.AsFiled, SourceCode.Create("IDS"), recorded));
            }

            return report;
        }

        /// <summary>A second security, for tests where a review swaps one name for another.</summary>
        public InstrumentId Joiner { get; } = InstrumentId.New();

        public Task<DatasetExport> ExportAsync(
            bool pointInTime = false,
            int history = 0,
            bool fundamentals = false,
            DateTimeOffset? knownAsOf = null)
        {
            DatasetRequest? request;
            string? problem;

            var valid = pointInTime
                ? DatasetRequest.TryCreatePointInTime(
                    Vn30, BarInterval.OneDay, From, To, out request, out problem, knownAsOfUtc: knownAsOf,
                    historySessions: history, fundamentals: fundamentals)
                : DatasetRequest.TryCreate(
                    Vn30, To, BarInterval.OneDay, From, To, out request, out problem, knownAsOfUtc: knownAsOf,
                    historySessions: history, fundamentals: fundamentals);

            Assert.True(valid, problem);

            return service.ExportAsync(request!, TestContext.Current.CancellationToken);
        }

        public Task<DatasetVerification> VerifyAsync(string datasetId, int version) =>
            service.VerifyAsync(datasetId, version, TestContext.Current.CancellationToken);

        /// <summary>Reads back every string column of a file the build wrote, by name.</summary>
        public async Task<IReadOnlyDictionary<string, string?[]>> ColumnsAsync(DatasetManifest manifest, string fileName)
        {
            var path = Path.Combine(Root, manifest.DatasetId, $"v{manifest.DatasetVersion}", fileName);

            await using var stream = File.OpenRead(path);
            await using var reader = await ParquetReader.CreateAsync(
                stream, cancellationToken: TestContext.Current.CancellationToken);
            using var group = reader.OpenRowGroupReader(0);

            var columns = new Dictionary<string, string?[]>(StringComparer.Ordinal);

            foreach (var field in reader.Schema.DataFields)
            {
                // The string columns are what ADR-031 decides; the dates and
                // counts are held by name only.
                if (field.ClrType != typeof(string))
                {
                    columns[field.Name] = [];
                    continue;
                }

                var values = new string?[group.RowCount];
                await group.ReadAsync(field, values.AsMemory(), cancellationToken: TestContext.Current.CancellationToken);
                columns[field.Name] = values;
            }

            return columns;
        }

        /// <summary>Reads back the session of every row the build wrote, in file order.</summary>
        public async Task<IReadOnlyList<DateOnly>> SessionsAsync(DatasetManifest manifest)
        {
            var path = Path.Combine(
                Root, manifest.DatasetId, $"v{manifest.DatasetVersion}", IDatasetStore.BarsFileName);

            await using var stream = File.OpenRead(path);
            await using var reader = await ParquetReader.CreateAsync(
                stream, cancellationToken: TestContext.Current.CancellationToken);
            using var group = reader.OpenRowGroupReader(0);

            var opened = new DateTime[group.RowCount];
            await group.ReadAsync<DateTime>(
                reader.Schema.DataFields[1], opened.AsMemory(), cancellationToken: TestContext.Current.CancellationToken);

            return [.. opened.Select(DateOnly.FromDateTime)];
        }
    }

    private sealed class FakeUniverseCatalog(
        IReadOnlyList<InstrumentId>? members,
        IReadOnlyList<UniverseSpell>? spells) : IUniverseCatalog
    {
        public Task<UniverseHistory> MembershipOverAsync(
            UniverseCode code,
            DateOnly fromDate,
            DateOnly toDate,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(spells is null
                ? UniverseHistory.Unknown(code, fromDate, toDate, UniverseUnknownReason.OutsideCoverage)
                : UniverseHistory.Known(code, fromDate, toDate, spells));

        public Task<UniverseConstituents> ConstituentsAsOfAsync(
            UniverseCode code,
            DateOnly asOf,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(members is null
                ? UniverseConstituents.Unknown(code, asOf, UniverseUnknownReason.NoCoverageDeclared)
                : UniverseConstituents.Known(code, asOf, members));
    }

    /// <summary>
    /// Supplies the one thing the export asks the instrument master for: the
    /// label an identifier carried.
    /// </summary>
    /// <remarks>
    /// Local rather than the shared in-memory master, which raises on the
    /// detail read because no import path exercises it. A fake that answered
    /// eleven questions to be asked one would hide which of them the export
    /// actually depends on.
    /// </remarks>
    private sealed class FakeInstrumentDetails(params InstrumentId[] known) : IInstrumentRepository
    {
        public Task<InstrumentDetail?> FindDetailByIdAsync(
            InstrumentId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(known.Contains(id)
                ? new InstrumentDetail(
                    id,
                    Ticker.Create("EXP"),
                    "Exported Company",
                    AssetType.Equity,
                    ExchangeCode.Create("HOSE"),
                    "Ho Chi Minh Stock Exchange",
                    CurrencyCode.Vnd,
                    InstrumentStatus.Listed,
                    ListedOn: null,
                    DelistedOn: null,
                    Classification: null,
                    Aliases: [])
                : null);

        public Task<Instrument?> FindByIdAsync(InstrumentId id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<Instrument?> FindActiveByTickerAsync(ExchangeId exchangeId, Ticker ticker, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<bool> IsTickerTakenAsync(ExchangeId exchangeId, Ticker ticker, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<IReadOnlyList<InstrumentSearchResult>> SearchAsync(InstrumentSearchCriteria criteria, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<IReadOnlyList<InstrumentSearchResult>> ListActiveByTickerAsync(Ticker ticker, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<InstrumentSearchResult?> FindResultByIdAsync(InstrumentId id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<InstrumentPage> ListAsync(InstrumentListCriteria criteria, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<InstrumentIdentifier?> FindIdentifierAsync(IdentifierValue value, SourceCode? source, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<IReadOnlyList<InstrumentIdentifier>> ListIdentifiersAsync(InstrumentId id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public Task<IReadOnlyList<RelatedInstrument>> ListRelatedAsync(InstrumentId id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the export.");

        public void Add(Instrument instrument) =>
            throw new NotSupportedException("The export never writes.");

        public void AddIdentifier(InstrumentIdentifier identifier) =>
            throw new NotSupportedException("The export never writes.");
    }

    private sealed class FakeBuild : IBuildIdentity
    {
        public string? Commit => "0123456789abcdef";
    }

    /// <summary>
    /// Serves one bar per weekday from a first session to the window's end, to
    /// whichever instrument asks.
    /// </summary>
    /// <remarks>
    /// Honours the query's bound from the newest end, as the real read does:
    /// the export's history is "the last N bars before admission", and a fake
    /// that took from the oldest end would pass a test the real query fails.
    /// </remarks>
    private sealed class FakeSeries(bool storeBars, DateOnly first)
        : IMarketDataQueryService
    {
        public Task<BarSeries> GetSeriesAsync(
            BarQuery query,
            CancellationToken cancellationToken = default)
        {
            if (!storeBars)
            {
                return Task.FromResult(new BarSeries(
                    query.InstrumentId, query.Interval, true, []));
            }

            var bars = Weekdays(first, To)
                .Select(day => new SeriesBar(
                    new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
                    100.5m,
                    101m,
                    99m,
                    100m,
                    1_000,
                    day == first ? null : 100_000m,
                    SourceCode.Create("TEST"),
                    1,
                    1m,
                    1m))
                .Where(bar => query.FromUtc is not { } start || bar.OpenedAtUtc >= start)
                .Where(bar => query.ToUtc is not { } end || bar.OpenedAtUtc < end)
                .TakeLast(query.Limit)
                .ToList();

            return Task.FromResult(new BarSeries(query.InstrumentId, query.Interval, true, bars));
        }

        public Task<IReadOnlyList<IngestionRun>> ListRecentRunsAsync(
            InstrumentId instrumentId,
            BarInterval interval,
            int limit,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<IngestionRun>>([]);

        private static IEnumerable<DateOnly> Weekdays(DateOnly from, DateOnly to)
        {
            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                {
                    yield return day;
                }
            }
        }
    }
}
