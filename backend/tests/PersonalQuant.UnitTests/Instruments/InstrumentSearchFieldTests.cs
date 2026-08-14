using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.UnitTests.Instruments;

/// <summary>
/// Covers the search columns the aggregate maintains alongside the ticker and
/// name. They are what the database matches against, so a path that changes an
/// instrument without updating them makes the security silently unfindable.
/// </summary>
public sealed class InstrumentSearchFieldTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Registering_populates_both_search_fields()
    {
        // Act
        var instrument = Register("FPT", "Công ty Cổ phần FPT");

        // Assert
        Assert.Equal("FPT", instrument.SearchTicker);
        Assert.Equal("CONG TY CO PHAN FPT", instrument.SearchName);
    }

    [Fact]
    public void Renaming_refolds_the_search_name()
    {
        // Arrange
        var instrument = Register("FPT", "FPT Corporation");

        // Act
        instrument.Rename("Công ty Cổ phần FPT", Now.AddDays(1));

        // Assert
        Assert.Equal("CONG TY CO PHAN FPT", instrument.SearchName);
    }

    [Fact]
    public void Changing_the_ticker_refolds_the_search_ticker()
    {
        // Arrange
        var instrument = Register("FPT", "FPT Corporation");
        instrument.List(new DateOnly(2026, 8, 20), Now.AddDays(1));

        // Act
        instrument.ChangeTicker(Ticker.Create("fpt2"), Now.AddDays(2));

        // Assert
        Assert.Equal("FPT2", instrument.SearchTicker);
    }

    [Fact]
    public void The_search_ticker_always_agrees_with_the_ticker()
    {
        // The one invariant that justifies storing the same characters twice.
        // Arrange
        var instrument = Register("FPT", "FPT Corporation");
        instrument.List(new DateOnly(2026, 8, 20), Now.AddDays(1));
        instrument.ChangeTicker(Ticker.Create("HPG"), Now.AddDays(2));

        // Assert
        Assert.Equal(instrument.Ticker.Value, instrument.SearchTicker);
    }

    [Fact]
    public void The_search_name_always_agrees_with_the_name()
    {
        // Arrange
        var instrument = Register("FPT", "FPT Corporation");
        instrument.Rename("Ngân hàng Ngoại thương", Now.AddDays(1));

        // Assert
        Assert.Equal(
            InstrumentSearchText.Normalise(instrument.Name),
            instrument.SearchName);
    }

    [Fact]
    public void An_instrument_can_be_listed_without_a_first_trading_date()
    {
        // Provider symbol lists routinely omit it. Refusing to record that a
        // security trades because its listing date is unsourced would be a
        // worse answer than recording it with the date left empty.
        // Arrange
        var instrument = Register("BSR", "Binh Son Refining and Petrochemical");

        // Act
        instrument.List(Now.AddDays(1));

        // Assert
        Assert.Equal(InstrumentStatus.Listed, instrument.Status);
        Assert.Null(instrument.ListedOn);
        Assert.True(instrument.IsActive);
    }

    [Fact]
    public void Listing_without_a_date_still_rejects_an_illegal_transition()
    {
        // The overload is a convenience, not a way around the state machine.
        // Arrange
        var instrument = Register("BSR", "Binh Son Refining and Petrochemical");
        instrument.List(Now.AddDays(1));

        // Act + Assert
        Assert.Throws<DomainStateException>(() => instrument.List(Now.AddDays(2)));
    }

    [Fact]
    public void An_instrument_listed_without_a_date_can_still_be_delisted()
    {
        // The delisting check compares against the listing date only when one
        // is known, so an unsourced listing date must not block the terminal
        // state.
        // Arrange
        var instrument = Register("BSR", "Binh Son Refining and Petrochemical");
        instrument.List(Now.AddDays(1));

        // Act
        instrument.Delist(new DateOnly(2026, 9, 1), Now.AddDays(2));

        // Assert
        Assert.Equal(InstrumentStatus.Delisted, instrument.Status);
        Assert.False(instrument.IsActive);
    }

    private static Instrument Register(string ticker, string name) =>
        Instrument.Register(
            ExchangeId.New(),
            Ticker.Create(ticker),
            name,
            AssetType.Equity,
            CurrencyCode.Vnd,
            Now);
}
