using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies the observation window a revision claims.
/// </summary>
/// <remarks>
/// The window is half-open — <c>[ObservedFromUtc, ObservedToUtc)</c> — and every
/// point-in-time answer the system gives rests on that. If the bounds were
/// inclusive at both ends two revisions would match the instant they meet at,
/// and a series would briefly report a period twice.
/// </remarks>
public sealed class BarRevisionTests
{
    private static readonly DateTimeOffset Period = new(2026, 8, 25, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = new(2026, 8, 26, 3, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T3 = new(2026, 8, 27, 3, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Source = SourceCode.Create("STUB");

    [Fact]
    public void A_snapshot_copies_the_bar_and_opens_its_window()
    {
        // Arrange
        var bar = Bar(close: 100m);

        // Act
        var revision = BarRevision.Snapshot(bar, T1);

        // Assert
        Assert.Equal(bar.InstrumentId, revision.InstrumentId);
        Assert.Equal(bar.Interval, revision.Interval);
        Assert.Equal(bar.OpenedAtUtc, revision.OpenedAtUtc);
        Assert.Equal(bar.Revision, revision.Revision);
        Assert.Equal(bar.Close, revision.Close);
        Assert.Equal(bar.Volume, revision.Volume);
        Assert.Equal(bar.Source, revision.Source);
        Assert.Equal(bar.TransformationVersion, revision.TransformationVersion);
        Assert.Equal(bar.ValidationVersion, revision.ValidationVersion);
        Assert.Equal(T1, revision.ObservedFromUtc);
        Assert.Null(revision.ObservedToUtc);
        Assert.True(revision.IsCurrent);
    }

    [Fact]
    public void The_window_is_inclusive_at_the_instant_it_opened()
    {
        // The instant of observation is when the system began holding the
        // statement, so a query as of exactly then must see it. Excluding it
        // would make a bar ingested at 03:00 invisible to a query for 03:00.
        var revision = BarRevision.Snapshot(Bar(close: 100m), T1);

        Assert.True(revision.WasKnownAt(T1));
    }

    [Fact]
    public void The_window_is_exclusive_at_the_instant_it_closed()
    {
        // Arrange
        var revision = BarRevision.Snapshot(Bar(close: 100m), T1);
        revision.Supersede(T3);

        // Assert — the closing edge belongs to the successor, not to this one,
        // which is what stops both matching at the instant they meet.
        Assert.True(revision.WasKnownAt(T3.AddTicks(-1)));
        Assert.False(revision.WasKnownAt(T3));
    }

    [Fact]
    public void A_window_does_not_cover_an_instant_before_it_opened()
    {
        var revision = BarRevision.Snapshot(Bar(close: 100m), T1);

        Assert.False(revision.WasKnownAt(T1.AddTicks(-1)));
    }

    [Fact]
    public void An_open_window_covers_every_instant_after_it_opened()
    {
        var revision = BarRevision.Snapshot(Bar(close: 100m), T1);

        Assert.True(revision.WasKnownAt(T3));
        Assert.True(revision.WasKnownAt(T3.AddYears(10)));
    }

    [Fact]
    public void Superseding_records_the_instant_and_ends_the_window()
    {
        // Arrange
        var revision = BarRevision.Snapshot(Bar(close: 100m), T1);

        // Act
        revision.Supersede(T3);

        // Assert
        Assert.Equal(T3, revision.ObservedToUtc);
        Assert.False(revision.IsCurrent);
    }

    [Fact]
    public void A_window_cannot_be_closed_twice()
    {
        // A second close would move a boundary that a stored answer already
        // depends on, which is the one edit an append-only history must refuse.
        var revision = BarRevision.Snapshot(Bar(close: 100m), T1);
        revision.Supersede(T3);

        Assert.Throws<DomainStateException>(() => revision.Supersede(T3.AddDays(1)));
    }

    [Fact]
    public void A_window_cannot_close_before_it_opened()
    {
        var revision = BarRevision.Snapshot(Bar(close: 100m), T3);

        Assert.Throws<DomainValidationException>(() => revision.Supersede(T1));
    }

    [Fact]
    public void A_zero_width_window_is_allowed_and_covers_nothing()
    {
        // Two restatements inside one run share an instant. The window is
        // legal, holds no instant, and must simply never be returned — which is
        // what the half-open bounds already guarantee.
        var revision = BarRevision.Snapshot(Bar(close: 100m), T1);

        revision.Supersede(T1);

        Assert.False(revision.WasKnownAt(T1));
    }

    [Fact]
    public void A_local_time_observation_stamp_is_rejected()
    {
        var local = new DateTimeOffset(2026, 8, 26, 10, 0, 0, TimeSpan.FromHours(7));

        Assert.Throws<DomainValidationException>(
            () => BarRevision.Snapshot(Bar(close: 100m), local));
    }

    [Fact]
    public void A_local_time_supersede_stamp_is_rejected()
    {
        var revision = BarRevision.Snapshot(Bar(close: 100m), T1);
        var local = new DateTimeOffset(2026, 8, 27, 10, 0, 0, TimeSpan.FromHours(7));

        Assert.Throws<DomainValidationException>(() => revision.Supersede(local));
    }

    [Fact]
    public void Adjacent_windows_cover_every_instant_exactly_once()
    {
        // The property the whole design rests on: one instant, one answer.
        var bar = Bar(close: 100m);
        var first = BarRevision.Snapshot(bar, T1);

        bar.Revise(
            Price.Create(100m),
            Price.Create(110m),
            Price.Create(95m),
            Price.Create(101m),
            1_000,
            null,
            Source,
            T3);

        first.Supersede(T3);
        var second = BarRevision.Snapshot(bar, T3);

        foreach (var instant in new[] { T1, T1.AddHours(6), T3.AddTicks(-1), T3, T3.AddDays(1) })
        {
            var matches = new[] { first, second }.Count(revision => revision.WasKnownAt(instant));

            Assert.Equal(1, matches);
        }

        Assert.False(first.WasKnownAt(T1.AddTicks(-1)));
        Assert.False(second.WasKnownAt(T1.AddTicks(-1)));
    }

    private static OhlcvBar Bar(decimal close) =>
        OhlcvBar.Record(
            InstrumentId.New(),
            BarInterval.OneDay,
            Period,
            Price.Create(100m),
            Price.Create(110m),
            Price.Create(95m),
            Price.Create(close),
            1_000,
            null,
            Source,
            T1);
}
