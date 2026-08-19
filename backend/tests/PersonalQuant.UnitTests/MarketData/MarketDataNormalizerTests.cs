using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies that every provider row comes back either as a bar or as a
/// rejection with a reason.
/// </summary>
/// <remarks>
/// Nothing is dropped silently and nothing is repaired. Clamping a high up to
/// the close would turn a visible provider fault into a plausible bar that
/// every later phase computes on.
/// </remarks>
public sealed class MarketDataNormalizerTests
{
    private static readonly DateTimeOffset From = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ingested = new(2026, 8, 29, 1, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Source = SourceCode.Create("TEST");

    private readonly MarketDataNormalizer _normalizer = new();

    [Fact]
    public void A_clean_response_becomes_bars()
    {
        var request = Request();

        // Act
        var result = _normalizer.Normalize(
            request,
            Source,
            [Row(From, 100m, 110m, 95m, 105m), Row(From.AddDays(1), 105m, 115m, 100m, 112m)],
            Ingested);

        // Assert
        Assert.Empty(result.Rejected);
        Assert.Equal(2, result.Accepted.Count);
        Assert.Equal(From.AddDays(1), result.LastAcceptedOpenedAtUtc);
    }

    [Fact]
    public void Accepted_bars_come_back_oldest_first_whatever_order_they_arrived_in()
    {
        // Downstream code compares consecutive bars. An order that depended on
        // the provider's response would make that comparison provider-specific.
        var request = Request();

        var result = _normalizer.Normalize(
            request,
            Source,
            [Row(From.AddDays(2), 1m, 1m, 1m, 1m), Row(From, 1m, 1m, 1m, 1m), Row(From.AddDays(1), 1m, 1m, 1m, 1m)],
            Ingested);

        Assert.Equal(
            [From, From.AddDays(1), From.AddDays(2)],
            result.Accepted.Select(bar => bar.OpenedAtUtc));
    }

    [Fact]
    public void A_row_outside_the_requested_range_is_rejected()
    {
        // Providers over-return. Storing the extra periods would put bars
        // outside the range the checkpoint is about to claim was covered.
        var request = Request();

        var result = _normalizer.Normalize(
            request, Source, [Row(To, 100m, 110m, 95m, 105m)], Ingested);

        Assert.Empty(result.Accepted);
        Assert.Equal(
            BarRejectionReason.OutsideRequestedRange,
            Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public void A_misaligned_row_is_rejected()
    {
        var request = Request();

        var result = _normalizer.Normalize(
            request, Source, [Row(From.AddHours(2), 100m, 110m, 95m, 105m)], Ingested);

        Assert.Equal(
            BarRejectionReason.MisalignedTimestamp,
            Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public void A_period_repeated_within_one_response_is_rejected_once()
    {
        // The first occurrence is kept and the repeat reported, rather than
        // the last quietly winning.
        var request = Request();

        var result = _normalizer.Normalize(
            request,
            Source,
            [Row(From, 100m, 110m, 95m, 105m), Row(From, 200m, 210m, 195m, 205m)],
            Ingested);

        Assert.Equal(100m, Assert.Single(result.Accepted).Open.Value);
        Assert.Equal(
            BarRejectionReason.DuplicateWithinBatch,
            Assert.Single(result.Rejected).Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_row_with_an_unusable_price_is_rejected(decimal close)
    {
        var request = Request();

        var result = _normalizer.Normalize(
            request, Source, [Row(From, 100m, 110m, 95m, close)], Ingested);

        Assert.Equal(BarRejectionReason.UnusablePrice, Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public void A_row_whose_prices_contradict_each_other_is_rejected()
    {
        var request = Request();

        var result = _normalizer.Normalize(
            request, Source, [Row(From, 100m, 104m, 95m, 105m)], Ingested);

        Assert.Equal(
            BarRejectionReason.InconsistentPrices,
            Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public void A_row_with_a_negative_volume_is_rejected_as_a_quantity_problem()
    {
        // The distinction matters for diagnosis: a wall of price rejections is
        // a swapped column, a wall of quantity rejections is a different bug.
        var request = Request();

        var result = _normalizer.Normalize(
            request,
            Source,
            [Row(From, 100m, 110m, 95m, 105m) with { Volume = -1 }],
            Ingested);

        Assert.Equal(
            BarRejectionReason.UnusableQuantity,
            Assert.Single(result.Rejected).Reason);
    }

    [Fact]
    public void A_rejection_carries_the_row_that_caused_it()
    {
        // A count alone cannot be investigated; the offending values usually
        // are the diagnosis.
        var request = Request();
        var row = Row(From, 100m, 104m, 95m, 105m);

        var rejection = Assert.Single(
            _normalizer.Normalize(request, Source, [row], Ingested).Rejected);

        Assert.Same(row, rejection.Bar);
        Assert.False(string.IsNullOrWhiteSpace(rejection.Detail));
    }

    [Fact]
    public void An_empty_response_normalises_to_nothing()
    {
        var result = _normalizer.Normalize(Request(), Source, [], Ingested);

        Assert.Empty(result.Accepted);
        Assert.Empty(result.Rejected);
        Assert.Null(result.LastAcceptedOpenedAtUtc);
    }

    [Fact]
    public void A_timestamp_in_another_offset_is_converted_rather_than_rejected()
    {
        // The same instant written in local time is still that instant. A
        // provider is entitled to its own offset; it is not entitled to its
        // own period boundaries.
        var request = Request();
        var local = new DateTimeOffset(2026, 8, 25, 7, 0, 0, TimeSpan.FromHours(7));

        var result = _normalizer.Normalize(
            request, Source, [Row(local, 100m, 110m, 95m, 105m)], Ingested);

        Assert.Equal(
            new DateTimeOffset(2026, 8, 25, 0, 0, 0, TimeSpan.Zero),
            Assert.Single(result.Accepted).OpenedAtUtc);
    }

    private static ProviderBar Row(
        DateTimeOffset openedAtUtc,
        decimal open,
        decimal high,
        decimal low,
        decimal close) =>
        new(openedAtUtc, open, high, low, close, 1_000, null);

    private static MarketDataRequest Request()
    {
        Assert.True(MarketDataRequest.TryCreate(
            InstrumentId.New(),
            Ticker.Create("FPT"),
            ExchangeCode.Create("HOSE"),
            BarInterval.OneDay,
            From,
            To,
            out var request,
            out var problem),
            problem);

        return request;
    }
}
