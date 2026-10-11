using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.Terminal;

namespace PersonalQuant.UnitTests.Terminal;

public sealed class WatchlistTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("banks")]
    [InlineData("vn30-core")]
    [InlineData("q4_2026")]
    [InlineData("7")]
    public void A_name_that_needs_no_quoting_anywhere_is_accepted(string name)
    {
        Assert.Equal(name, Watchlist.Create(name, Now).Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Banks")]
    [InlineData("has space")]
    [InlineData("-leading")]
    [InlineData("dots.are.paths")]
    [InlineData("slash/name")]
    public void A_name_a_url_or_a_shell_would_mangle_is_refused(string name)
    {
        // The name is a URL segment, a CLI argument and a console word at once.
        Assert.Throws<DomainValidationException>(() => Watchlist.Create(name, Now));
    }

    [Fact]
    public void A_name_longer_than_the_bound_is_refused()
    {
        Assert.Throws<DomainValidationException>(
            () => Watchlist.Create(new string('a', Watchlist.MaxNameLength + 1), Now));
    }

    [Fact]
    public void Adding_twice_leaves_the_list_as_one_add_would()
    {
        var list = Watchlist.Create("banks", Now);
        var instrument = InstrumentId.New();

        Assert.True(list.Add(instrument, Now));
        Assert.False(list.Add(instrument, Now.AddMinutes(1)));

        Assert.Single(list.Entries);
        Assert.Equal(Now, list.UpdatedAtUtc);
    }

    [Fact]
    public void Entries_keep_the_order_they_were_added_in_across_a_removal()
    {
        var list = Watchlist.Create("banks", Now);
        var (a, b, c) = (InstrumentId.New(), InstrumentId.New(), InstrumentId.New());

        list.Add(a, Now);
        list.Add(b, Now);
        list.Remove(a, Now);
        list.Add(c, Now);

        Assert.Equal([b, c], list.Entries.Select(entry => entry.InstrumentId));
        Assert.True(list.Entries[0].Position < list.Entries[1].Position);
    }

    [Fact]
    public void Removing_what_is_not_there_changes_nothing()
    {
        var list = Watchlist.Create("banks", Now);

        Assert.False(list.Remove(InstrumentId.New(), Now.AddHours(1)));
        Assert.Equal(Now, list.UpdatedAtUtc);
    }

    [Fact]
    public void A_full_list_refuses_another_instrument()
    {
        var list = Watchlist.Create("full", Now);

        for (var index = 0; index < Watchlist.MaxEntries; index++)
        {
            list.Add(InstrumentId.New(), Now);
        }

        Assert.Throws<DomainValidationException>(() => list.Add(InstrumentId.New(), Now));
    }

    [Fact]
    public void Timestamps_must_be_utc()
    {
        var local = new DateTimeOffset(2026, 10, 11, 16, 0, 0, TimeSpan.FromHours(7));

        Assert.Throws<DomainValidationException>(() => Watchlist.Create("banks", local));
        Assert.Throws<DomainValidationException>(() => Watchlist.Create("banks", Now).Add(InstrumentId.New(), local));
    }
}
