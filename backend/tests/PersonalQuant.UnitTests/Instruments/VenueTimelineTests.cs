using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.UnitTests.Instruments;

/// <summary>
/// Verifies which venue a session traded on, from a recorded venue history.
/// </summary>
public sealed class VenueTimelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 0, 0, 0, TimeSpan.Zero);
    private static readonly InstrumentId Instrument = InstrumentId.New();
    private static readonly ExchangeId Upcom = ExchangeId.New();
    private static readonly ExchangeId Hose = ExchangeId.New();

    /// <summary>BSR-like: UPCOM until 2025-01-20, HOSE from that session on.</summary>
    private static VenueTimeline Moved() =>
        new(
            Hose,
            [
                InstrumentVenueSpell.Record(Instrument, Hose, new DateOnly(2025, 1, 20), null, Now),
                InstrumentVenueSpell.Record(Instrument, Upcom, null, new DateOnly(2025, 1, 20), Now),
            ]);

    [Fact]
    public void A_session_before_the_move_is_on_the_old_venue_and_the_move_day_on_the_new()
    {
        var timeline = Moved();

        Assert.Equal(Upcom, timeline.On(new DateOnly(2025, 1, 17)));
        Assert.Equal(Hose, timeline.On(new DateOnly(2025, 1, 20)));
        Assert.True(timeline.HasMoved);
    }

    [Fact]
    public void An_instrument_with_no_history_is_on_its_current_venue_throughout()
    {
        var timeline = new VenueTimeline(Hose, []);

        Assert.Equal(Hose, timeline.On(new DateOnly(2016, 5, 27)));
        Assert.False(timeline.HasMoved);
        Assert.Equal([(Hose, new DateOnly(2016, 1, 4), new DateOnly(2016, 1, 8))],
            timeline.Stretches(new DateOnly(2016, 1, 4), new DateOnly(2016, 1, 8)));
    }

    [Fact]
    public void A_window_across_the_move_splits_at_the_first_session_on_the_new_venue()
    {
        var stretches = Moved().Stretches(new DateOnly(2025, 1, 13), new DateOnly(2025, 1, 24));

        Assert.Equal(
            [
                (Upcom, new DateOnly(2025, 1, 13), new DateOnly(2025, 1, 19)),
                (Hose, new DateOnly(2025, 1, 20), new DateOnly(2025, 1, 24)),
            ],
            stretches);
    }

    [Fact]
    public void A_window_inside_one_spell_is_one_stretch() =>
        Assert.Single(Moved().Stretches(new DateOnly(2025, 3, 3), new DateOnly(2025, 3, 7)));

    [Fact]
    public void A_spell_that_ends_where_it_starts_is_refused() =>
        Assert.Throws<DomainValidationException>(() => InstrumentVenueSpell.Record(
            Instrument, Hose, new DateOnly(2025, 1, 20), new DateOnly(2025, 1, 20), Now));
}
