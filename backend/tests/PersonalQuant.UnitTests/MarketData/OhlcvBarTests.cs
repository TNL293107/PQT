using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies the invariants a single bar can enforce about itself.
/// </summary>
/// <remarks>
/// Every failure below is a real provider fault — a swapped column pair, a
/// high taken from a different period, a volume field holding turnover — and
/// every one of them is invisible once the row is stored.
/// </remarks>
public sealed class OhlcvBarTests
{
    private static readonly DateTimeOffset Period = new(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Ingested = new(2026, 8, 26, 1, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Source = SourceCode.Create("TEST");

    [Fact]
    public void A_recorded_bar_carries_its_values_and_closes_one_interval_later()
    {
        // Act
        var bar = Record(open: 100m, high: 110m, low: 95m, close: 105m, volume: 1_000);

        // Assert
        Assert.Equal(100m, bar.Open.Value);
        Assert.Equal(110m, bar.High.Value);
        Assert.Equal(95m, bar.Low.Value);
        Assert.Equal(105m, bar.Close.Value);
        Assert.Equal(1_000, bar.Volume);
        Assert.Equal(Period.AddDays(1), bar.ClosedAtUtc);
        Assert.Equal(0, bar.Revision);
        Assert.Null(bar.RevisedAtUtc);
    }

    [Theory]
    [InlineData(100, 90, 95, 98, 1_000, null)]   // high below the low
    // High below the close: the classic swapped-column failure. It produces a
    // bar that looks ordinary and prices a period the market never traded at.
    [InlineData(100, 104, 95, 105, 1_000, null)]
    [InlineData(100, 110, 101, 105, 1_000, null)] // low above the open
    [InlineData(100, 110, 95, 105, -1, null)]     // negative volume
    // Turnover without volume: cash changing hands with nothing traded means
    // the two fields came from different periods.
    [InlineData(100, 100, 100, 100, 0, 5_000d)]
    public void An_inconsistent_bar_is_rejected(
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume,
        double? turnover) =>
        Assert.Throws<DomainValidationException>(
            () => Record(open, high, low, close, volume, (decimal?)turnover));

    [Fact]
    public void A_bar_with_no_volume_is_accepted()
    {
        // An illiquid UPCOM security can go a whole session without a trade
        // and the exchange still publishes the period. The bar is flat as well,
        // which is legitimate on its own: a security that traded once, or hit
        // its price limit and stayed there.
        var bar = Record(open: 100m, high: 100m, low: 100m, close: 100m, volume: 0);

        Assert.Equal(0, bar.Volume);
    }

    [Fact]
    public void A_misaligned_opening_instant_is_rejected() =>
        Assert.Throws<DomainValidationException>(() => OhlcvBar.Record(
            InstrumentId.New(),
            BarInterval.OneDay,
            Period.AddHours(2),
            Price.Create(100m),
            Price.Create(100m),
            Price.Create(100m),
            Price.Create(100m),
            0,
            null,
            Source,
            Ingested));

    [Fact]
    public void A_bar_without_an_instrument_is_rejected() =>
        Assert.Throws<DomainValidationException>(() => OhlcvBar.Record(
            default,
            BarInterval.OneDay,
            Period,
            Price.Create(100m),
            Price.Create(100m),
            Price.Create(100m),
            Price.Create(100m),
            0,
            null,
            Source,
            Ingested));

    [Fact]
    public void A_local_time_ingestion_stamp_is_rejected() =>
        Assert.Throws<DomainValidationException>(() => OhlcvBar.Record(
            InstrumentId.New(),
            BarInterval.OneDay,
            Period,
            Price.Create(100m),
            Price.Create(100m),
            Price.Create(100m),
            Price.Create(100m),
            0,
            null,
            Source,
            new DateTimeOffset(2026, 8, 26, 8, 0, 0, TimeSpan.FromHours(7))));

    [Fact]
    public void Restating_a_bar_with_different_values_counts_and_stamps_it()
    {
        var bar = Record(open: 100m, high: 110m, low: 95m, close: 105m, volume: 1_000);
        var later = Ingested.AddDays(1);

        // Act
        var changed = bar.Revise(
            Price.Create(100m),
            Price.Create(112m),
            Price.Create(95m),
            Price.Create(108m),
            1_200,
            null,
            Source,
            later);

        // Assert
        Assert.True(changed);
        Assert.Equal(1, bar.Revision);
        Assert.Equal(later, bar.RevisedAtUtc);
        Assert.Equal(108m, bar.Close.Value);
        Assert.Equal(Ingested, bar.IngestedAtUtc);
    }

    [Fact]
    public void A_restatement_that_contradicts_itself_is_rejected()
    {
        // The invariants apply to a correction exactly as they do to the
        // original: a restatement is not a way around them.
        var bar = Record(open: 100m, high: 110m, low: 95m, close: 105m, volume: 1_000);

        Assert.Throws<DomainValidationException>(() => bar.Revise(
            Price.Create(100m),
            Price.Create(99m),
            Price.Create(95m),
            Price.Create(105m),
            1_000,
            null,
            Source,
            Ingested.AddDays(1)));
    }

    [Fact]
    public void A_second_source_agreeing_on_the_values_is_not_a_revision()
    {
        // Re-fetching a range that has not moved is the normal case and is not
        // a revision, whichever source reports it.
        //
        // A revision is the ordinal identity of one statement of a fact. Two
        // providers reporting the same numbers is one statement corroborated,
        // not two — and counting it would put a restatement into the
        // observation history where no value moved, which is exactly what
        // makes a point-in-time read untruthful.
        var bar = Record(open: 100m, high: 110m, low: 95m, close: 105m, volume: 1_000);
        bar.MarkValidated(DataRules.ValidationVersion);

        // Act
        var changed = bar.Revise(
            Price.Create(100m),
            Price.Create(110m),
            Price.Create(95m),
            Price.Create(105m),
            1_000,
            null,
            SourceCode.Create("OTHER"),
            Ingested.AddDays(1));

        // Assert
        Assert.False(changed);
        Assert.Equal(0, bar.Revision);
        Assert.Null(bar.RevisedAtUtc);

        // The bar stays attributed to whichever source produced it. A provider
        // that agreed with a number did not produce it.
        Assert.Equal("TEST", bar.Source.Value);

        // And nothing moved, so the quality verdict still applies. Clearing it
        // would send an unchanged bar back through validation on every fetch
        // from every other source.
        Assert.Equal(DataRules.ValidationVersion, bar.ValidationVersion);
    }

    [Fact]
    public void A_second_source_that_moves_the_values_revises_and_takes_the_bar()
    {
        // Two providers disagreeing about a close is a real event. The bar is
        // revised and re-attributed; the disagreement itself is recorded as a
        // quality finding by the ingestion pipeline, not here.
        var bar = Record(open: 100m, high: 110m, low: 95m, close: 105m, volume: 1_000);
        var later = Ingested.AddDays(1);

        // Act
        var changed = bar.Revise(
            Price.Create(100m),
            Price.Create(110m),
            Price.Create(95m),
            Price.Create(106m),
            1_000,
            null,
            SourceCode.Create("OTHER"),
            later);

        // Assert
        Assert.True(changed);
        Assert.Equal(1, bar.Revision);
        Assert.Equal(later, bar.RevisedAtUtc);
        Assert.Equal(106m, bar.Close.Value);
        Assert.Equal("OTHER", bar.Source.Value);
    }

    private static OhlcvBar Record(
        decimal open,
        decimal high,
        decimal low,
        decimal close,
        long volume = 1_000,
        decimal? turnover = null) =>
        OhlcvBar.Record(
            InstrumentId.New(),
            BarInterval.OneDay,
            Period,
            Price.Create(open),
            Price.Create(high),
            Price.Create(low),
            Price.Create(close),
            volume,
            turnover,
            Source,
            Ingested);
}
