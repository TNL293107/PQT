using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies the two validated request types: what may be asked of a provider,
/// and what may be asked of the stored series.
/// </summary>
public sealed class MarketDataRequestTests
{
    private static readonly DateTimeOffset From = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_valid_request_reports_how_many_periods_it_covers()
    {
        Assert.True(TryCreate(From, To, out var request, out _));
        Assert.Equal(5, request!.Periods);
    }

    [Fact]
    public void The_range_is_half_open()
    {
        // Two adjacent requests tile the timeline exactly: no period belongs
        // to both, and none belongs to neither.
        Assert.True(TryCreate(From, To, out var request, out _));

        Assert.True(request!.Covers(From));
        Assert.False(request.Covers(To));
        Assert.False(request.Covers(From.AddDays(-1)));
    }

    [Fact]
    public void An_unaligned_edge_is_rejected()
    {
        // An unaligned edge would put a period partly inside two requests, and
        // deduplication would then depend on which ran first.
        Assert.False(TryCreate(From.AddHours(3), To, out _, out var problem));
        Assert.Contains("period boundary", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_range_expressed_in_local_time_is_rejected()
    {
        var local = new DateTimeOffset(2026, 8, 24, 7, 0, 0, TimeSpan.FromHours(7));

        Assert.False(TryCreate(local, To, out _, out var problem));
        Assert.Contains("UTC", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_or_inverted_range_is_rejected()
    {
        Assert.False(TryCreate(From, From, out _, out _));
        Assert.False(TryCreate(To, From, out _, out _));
    }

    [Fact]
    public void A_request_beyond_the_period_ceiling_is_rejected()
    {
        // Providers page, rate-limit and time out. Letting the request be made
        // means finding that out through a timeout rather than a clear refusal.
        var tooLong = From.AddMinutes(MarketDataRequest.MaxPeriods + 1);

        Assert.False(MarketDataRequest.TryCreate(
            InstrumentId.New(),
            Ticker.Create("FPT"),
            ExchangeCode.Create("HOSE"),
            BarInterval.OneMinute,
            From,
            tooLong,
            out _,
            out var problem));

        Assert.Contains("periods", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_request_without_a_ticker_or_venue_is_rejected()
    {
        // A provider is addressed in its own symbology and knows nothing about
        // this system's keys.
        Assert.False(MarketDataRequest.TryCreate(
            InstrumentId.New(),
            null,
            ExchangeCode.Create("HOSE"),
            BarInterval.OneDay,
            From,
            To,
            out _,
            out _));
    }

    [Fact]
    public void A_bar_query_defaults_its_bound()
    {
        Assert.True(BarQuery.TryCreate(
            InstrumentId.New(), BarInterval.OneDay, null, null, null, out var query, out _));

        Assert.Equal(BarQuery.DefaultLimit, query!.Limit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(BarQuery.MaxLimit + 1)]
    public void A_bar_query_outside_the_bound_is_rejected(int limit)
    {
        // A series is the one table that grows without limit, and this read is
        // reachable from an anonymous caller.
        Assert.False(BarQuery.TryCreate(
            InstrumentId.New(), BarInterval.OneDay, null, null, limit, out _, out _));
    }

    [Fact]
    public void A_bar_query_window_may_be_open_at_either_end()
    {
        Assert.True(BarQuery.TryCreate(
            InstrumentId.New(), BarInterval.OneDay, From, null, 10, out var openEnd, out _));
        Assert.Null(openEnd!.ToUtc);

        Assert.True(BarQuery.TryCreate(
            InstrumentId.New(), BarInterval.OneDay, null, To, 10, out var openStart, out _));
        Assert.Null(openStart!.FromUtc);
    }

    [Fact]
    public void A_bar_query_window_that_ends_before_it_starts_is_rejected() =>
        Assert.False(BarQuery.TryCreate(
            InstrumentId.New(), BarInterval.OneDay, To, From, 10, out _, out _));

    [Fact]
    public void A_bar_query_normalises_its_window_to_utc()
    {
        var local = new DateTimeOffset(2026, 8, 24, 7, 0, 0, TimeSpan.FromHours(7));

        Assert.True(BarQuery.TryCreate(
            InstrumentId.New(), BarInterval.OneDay, local, null, 10, out var query, out _));

        Assert.Equal(TimeSpan.Zero, query!.FromUtc!.Value.Offset);
    }

    [Fact]
    public void An_ingestion_instruction_may_leave_both_ends_open()
    {
        // The usual case: resume from the checkpoint, stop at the last period
        // that has finished.
        Assert.True(IngestionInstruction.TryCreate(
            InstrumentId.New(), BarInterval.OneDay, null, null, null, out var instruction, out _));

        Assert.Null(instruction!.FromUtc);
        Assert.Null(instruction.ToUtc);
        Assert.Null(instruction.Source);
    }

    [Fact]
    public void An_ingestion_instruction_over_an_inverted_range_is_rejected() =>
        Assert.False(IngestionInstruction.TryCreate(
            InstrumentId.New(), BarInterval.OneDay, null, To, From, out _, out _));

    [Fact]
    public void An_undeclared_resolution_is_rejected_everywhere()
    {
        Assert.False(TryCreate(From, To, out _, out _, BarInterval.Unspecified));

        Assert.False(BarQuery.TryCreate(
            InstrumentId.New(), BarInterval.Unspecified, null, null, null, out _, out _));

        Assert.False(IngestionInstruction.TryCreate(
            InstrumentId.New(), BarInterval.Unspecified, null, null, null, out _, out _));
    }

    private static bool TryCreate(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        out MarketDataRequest? request,
        out string? problem,
        BarInterval interval = BarInterval.OneDay) =>
        MarketDataRequest.TryCreate(
            InstrumentId.New(),
            Ticker.Create("FPT"),
            ExchangeCode.Create("HOSE"),
            interval,
            fromUtc,
            toUtc,
            out request,
            out problem);
}
