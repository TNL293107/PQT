using PersonalQuant.Application.MarketData;
using PersonalQuant.Infrastructure.Universes;

namespace PersonalQuant.UnitTests.Universes;

/// <summary>
/// Verifies the file-backed universe source.
/// </summary>
/// <remarks>
/// The contract is parse, or fail loudly. A membership file states which
/// securities a strategy could have chosen from on a date, so a row read wrong
/// is not a formatting problem — it changes the answer to that question, and
/// nothing downstream can tell that it did.
/// </remarks>
public sealed class FileUniverseMembershipProviderTests : IDisposable
{
    private const string UniverseHeader = "code,name,kind,coverage_from,coverage_until";
    private const string MembershipHeader =
        "universe_code,symbol,effective_from,effective_to,announced_on";

    private readonly string _root = Directory.CreateTempSubdirectory("pqt-universe-file-").FullName;

    [Fact]
    public async Task A_universe_file_is_parsed_with_its_coverage_claim()
    {
        WriteUniverses(UniverseHeader, "VN30,VN30 Index,Index,2024-01-02,2026-01-02");

        var universe = Assert.Single(await Provider().ListUniversesAsync(
            TestContext.Current.CancellationToken));

        Assert.Equal("VN30", universe.Code);
        Assert.Equal("VN30 Index", universe.Name);
        Assert.Equal("Index", universe.Kind);
        Assert.Equal(new DateOnly(2024, 1, 2), universe.CoverageFrom);
        Assert.Equal(new DateOnly(2026, 1, 2), universe.CoverageUntil);
    }

    [Fact]
    public async Task A_blank_coverage_claim_is_read_as_no_claim()
    {
        // Not as a claim covering everything. A source that says nothing about
        // its own completeness has said nothing, and the universe it defines
        // answers every as-of read with "unknown" until somebody does.
        WriteUniverses(UniverseHeader, "VN30,VN30 Index,Index,,");

        var universe = Assert.Single(await Provider().ListUniversesAsync(
            TestContext.Current.CancellationToken));

        Assert.Null(universe.CoverageFrom);
        Assert.Null(universe.CoverageUntil);
    }

    [Fact]
    public async Task A_membership_file_is_parsed_into_spells()
    {
        WriteMemberships(
            MembershipHeader,
            "VN30,FPT.HM,2024-01-02,,2023-12-15",
            "VN30,VNM.HM,2024-01-02,2024-07-01,");

        var spells = await Provider().ListMembershipsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, spells.Count);
        Assert.Equal("FPT.HM", spells[0].Symbol);
        Assert.Null(spells[0].EffectiveTo);
        Assert.Equal(new DateOnly(2023, 12, 15), spells[0].AnnouncedOn);
        Assert.Equal(new DateOnly(2024, 7, 1), spells[1].EffectiveTo);
        Assert.Null(spells[1].AnnouncedOn);
    }

    [Fact]
    public async Task Columns_are_matched_by_name_rather_than_position()
    {
        // A reordered export must be read correctly rather than silently
        // transposed — which here would swap a joining date for a leaving one.
        WriteMemberships(
            "announced_on,effective_to,effective_from,symbol,universe_code",
            "2023-12-15,2024-07-01,2024-01-02,VNM.HM,VN30");

        var spell = Assert.Single(await Provider().ListMembershipsAsync(
            TestContext.Current.CancellationToken));

        Assert.Equal("VN30", spell.UniverseCode);
        Assert.Equal("VNM.HM", spell.Symbol);
        Assert.Equal(new DateOnly(2024, 1, 2), spell.EffectiveFrom);
        Assert.Equal(new DateOnly(2024, 7, 1), spell.EffectiveTo);
    }

    [Fact]
    public async Task Blank_lines_are_skipped()
    {
        WriteMemberships(MembershipHeader, string.Empty, "VN30,FPT.HM,2024-01-02,,", string.Empty);

        Assert.Single(await Provider().ListMembershipsAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_missing_column_fails_the_file()
    {
        WriteMemberships("universe_code,symbol", "VN30,FPT.HM");

        var error = await Assert.ThrowsAsync<MarketDataProviderException>(
            () => Provider().ListMembershipsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("effective_from", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreadable_date_fails_the_file_rather_than_the_row()
    {
        // Deliberately whole-file. A date this system cannot read is the
        // difference between a security being in an index and not, and a run
        // that dropped the row would record a membership history that is
        // missing a review nobody was told about.
        WriteMemberships(MembershipHeader, "VN30,FPT.HM,02/01/2024,,");

        var error = await Assert.ThrowsAsync<MarketDataProviderException>(
            () => Provider().ListMembershipsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("effective_from", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_file_is_reported_as_such()
    {
        var error = await Assert.ThrowsAsync<MarketDataProviderException>(
            () => Provider().ListUniversesAsync(TestContext.Current.CancellationToken));

        Assert.Contains("universes.csv", error.Message, StringComparison.Ordinal);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private FileUniverseMembershipProvider Provider() => new(_root);

    private void WriteUniverses(params string[] lines) =>
        File.WriteAllLines(Path.Combine(_root, "universes.csv"), lines);

    private void WriteMemberships(params string[] lines) =>
        File.WriteAllLines(Path.Combine(_root, "universe-memberships.csv"), lines);
}
