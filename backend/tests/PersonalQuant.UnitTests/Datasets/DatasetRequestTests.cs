using PersonalQuant.Application.Datasets;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Domain.Universes;

namespace PersonalQuant.UnitTests.Datasets;

/// <summary>
/// Verifies what an export request will and will not accept, and that the
/// identity it derives is a function of its parameters.
/// </summary>
public sealed class DatasetRequestTests
{
    private static readonly UniverseCode Vn30 = UniverseCode.Create("VN30");
    private static readonly DateOnly From = new(2016, 1, 1);
    private static readonly DateOnly To = new(2016, 12, 31);

    [Fact]
    public void A_window_that_ends_before_it_starts_is_refused()
    {
        var created = DatasetRequest.TryCreate(
            Vn30, To, BarInterval.OneDay, To, From, out _, out var problem);

        Assert.False(created);
        Assert.Contains("end on or after", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_single_day_window_is_allowed()
    {
        // Inclusive of both ends, unlike the bar query's half-open range. A
        // dataset for one session is a legitimate thing to ask for.
        Assert.True(DatasetRequest.TryCreate(
            Vn30, From, BarInterval.OneDay, From, From, out _, out _));
    }

    [Fact]
    public void A_universe_read_after_the_window_ends_is_refused()
    {
        // Survivorship bias assembled by hand: today's index members applied to
        // prices from a decade ago, which silently excludes everything that has
        // since been removed.
        var created = DatasetRequest.TryCreate(
            Vn30, new DateOnly(2026, 1, 1), BarInterval.OneDay, From, To, out _, out var problem);

        Assert.False(created);
        Assert.Contains("later membership to earlier prices", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_window_longer_than_the_bound_is_refused()
    {
        var created = DatasetRequest.TryCreate(
            Vn30,
            From,
            BarInterval.OneDay,
            From,
            From.AddYears(DatasetRequest.MaxYears + 1),
            out _,
            out var problem);

        Assert.False(created);
        Assert.Contains("may not span more than", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void The_export_default_is_the_strict_reading()
    {
        // Opposite to the chart's default, and deliberately. A dataset is run
        // against repeatedly by things nobody has written yet, so look-ahead
        // baked into one propagates into every experiment that reads it.
        Assert.True(DatasetRequest.TryCreate(
            Vn30, To, BarInterval.OneDay, From, To, out var request, out _));

        Assert.Equal(AnnouncementPolicy.Strict, request.AnnouncementPolicy);
        Assert.True(request.Adjusted);
    }

    [Fact]
    public void The_same_parameters_name_the_same_dataset()
    {
        Assert.True(DatasetRequest.TryCreate(
            Vn30, To, BarInterval.OneDay, From, To, out var one, out _));
        Assert.True(DatasetRequest.TryCreate(
            Vn30, To, BarInterval.OneDay, From, To, out var two, out _));

        Assert.Equal(one.DatasetId, two.DatasetId);
    }

    [Theory]
    [MemberData(nameof(DifferingRequests))]
    public void A_parameter_that_changes_the_data_changes_the_identifier(DatasetRequest other)
    {
        // Every one of these produces a different series. A dataset identifier
        // that collided across them would let one export silently overwrite the
        // version history of another.
        Assert.True(DatasetRequest.TryCreate(
            Vn30, To, BarInterval.OneDay, From, To, out var baseline, out _));

        Assert.NotEqual(baseline.DatasetId, other.DatasetId);
    }

    public static TheoryData<DatasetRequest> DifferingRequests()
    {
        var data = new TheoryData<DatasetRequest>();

        data.Add(Create(interval: BarInterval.OneHour));
        data.Add(Create(fromDate: From.AddDays(1)));
        data.Add(Create(toDate: To.AddDays(-1), universeAsOf: To.AddDays(-1)));
        data.Add(Create(universeAsOf: To.AddDays(-1)));
        data.Add(Create(adjusted: false));
        data.Add(Create(policy: AnnouncementPolicy.Permissive));
        data.Add(Create(knownAsOf: new DateTimeOffset(2016, 6, 1, 0, 0, 0, TimeSpan.Zero)));
        data.Add(Create(universe: UniverseCode.Create("VN100")));

        return data;
    }

    private static DatasetRequest Create(
        UniverseCode? universe = null,
        DateOnly? universeAsOf = null,
        BarInterval interval = BarInterval.OneDay,
        DateOnly? fromDate = null,
        DateOnly? toDate = null,
        bool adjusted = true,
        DateTimeOffset? knownAsOf = null,
        AnnouncementPolicy policy = AnnouncementPolicy.Strict)
    {
        Assert.True(DatasetRequest.TryCreate(
            universe ?? Vn30,
            universeAsOf ?? To,
            interval,
            fromDate ?? From,
            toDate ?? To,
            out var request,
            out var problem,
            adjusted,
            knownAsOf,
            policy),
            problem);

        return request;
    }
}
