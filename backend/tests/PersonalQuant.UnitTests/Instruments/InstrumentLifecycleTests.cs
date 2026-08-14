using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.UnitTests.Instruments;

/// <summary>
/// Covers the listing lifecycle. Every illegal transition is asserted, because
/// the state machine is what stops master data from drifting into a shape the
/// rest of the system cannot interpret.
/// </summary>
public sealed class InstrumentLifecycleTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly ListingDate = new(2026, 8, 20);

    [Fact]
    public void A_new_instrument_starts_pending_and_has_no_listing_dates()
    {
        // Act
        var instrument = Register();

        // Assert
        Assert.Equal(InstrumentStatus.Pending, instrument.Status);
        Assert.Null(instrument.ListedOn);
        Assert.Null(instrument.DelistedOn);
    }

    [Fact]
    public void Listing_records_the_first_trading_date()
    {
        // Arrange
        var instrument = Register();

        // Act
        instrument.List(ListingDate, Now.AddDays(1));

        // Assert
        Assert.Equal(InstrumentStatus.Listed, instrument.Status);
        Assert.Equal(ListingDate, instrument.ListedOn);
    }

    [Fact]
    public void A_listed_instrument_can_be_suspended_and_resumed()
    {
        // Arrange
        var instrument = Listed();

        // Act
        instrument.Suspend(Now.AddDays(2));
        var suspended = instrument.Status;
        instrument.Resume(Now.AddDays(3));

        // Assert
        Assert.Equal(InstrumentStatus.Suspended, suspended);
        Assert.Equal(InstrumentStatus.Listed, instrument.Status);
    }

    [Fact]
    public void A_suspended_instrument_can_be_delisted_directly()
    {
        // A halt that is never lifted is the normal path to delisting.
        // Arrange
        var instrument = Listed();
        instrument.Suspend(Now.AddDays(2));

        // Act
        instrument.Delist(new DateOnly(2026, 9, 1), Now.AddDays(3));

        // Assert
        Assert.Equal(InstrumentStatus.Delisted, instrument.Status);
    }

    [Fact]
    public void Delisting_is_terminal()
    {
        // Arrange
        var instrument = Delisted();

        // Act + Assert
        Assert.Throws<DomainStateException>(() =>
            instrument.Delist(new DateOnly(2026, 10, 1), Now.AddDays(10)));
    }

    [Fact]
    public void A_delisted_instrument_cannot_resume_trading()
    {
        // Arrange
        var instrument = Delisted();

        // Act + Assert
        Assert.Throws<DomainStateException>(() => instrument.Resume(Now.AddDays(10)));
    }

    [Fact]
    public void A_delisted_instrument_cannot_be_suspended()
    {
        // Arrange
        var instrument = Delisted();

        // Act + Assert
        Assert.Throws<DomainStateException>(() => instrument.Suspend(Now.AddDays(10)));
    }

    [Fact]
    public void A_pending_instrument_cannot_be_suspended()
    {
        // Arrange
        var instrument = Register();

        // Act + Assert
        Assert.Throws<DomainStateException>(() => instrument.Suspend(Now.AddDays(1)));
    }

    [Fact]
    public void A_pending_instrument_cannot_be_delisted()
    {
        // It never traded. Recording a delisting would imply a trading history
        // that does not exist.
        // Arrange
        var instrument = Register();

        // Act + Assert
        Assert.Throws<DomainStateException>(() =>
            instrument.Delist(ListingDate, Now.AddDays(1)));
    }

    [Fact]
    public void A_listed_instrument_cannot_be_listed_again()
    {
        // Arrange
        var instrument = Listed();

        // Act + Assert
        Assert.Throws<DomainStateException>(() =>
            instrument.List(ListingDate, Now.AddDays(2)));
    }

    [Fact]
    public void A_listed_instrument_cannot_resume()
    {
        // Arrange
        var instrument = Listed();

        // Act + Assert
        Assert.Throws<DomainStateException>(() => instrument.Resume(Now.AddDays(2)));
    }

    [Fact]
    public void Delisting_cannot_precede_listing()
    {
        // Arrange
        var instrument = Listed();

        // Act + Assert
        Assert.Throws<DomainValidationException>(() =>
            instrument.Delist(ListingDate.AddDays(-1), Now.AddDays(2)));
    }

    [Fact]
    public void Delisting_on_the_listing_date_is_allowed()
    {
        // A listing withdrawn on its first day is unusual but real.
        // Arrange
        var instrument = Listed();

        // Act
        instrument.Delist(ListingDate, Now.AddDays(2));

        // Assert
        Assert.Equal(ListingDate, instrument.DelistedOn);
    }

    [Theory]
    [InlineData(InstrumentStatus.Pending, true)]
    [InlineData(InstrumentStatus.Listed, true)]
    [InlineData(InstrumentStatus.Suspended, true)]
    [InlineData(InstrumentStatus.Delisted, false)]
    public void Only_a_delisted_instrument_releases_its_ticker(
        InstrumentStatus status,
        bool expectedActive)
    {
        // Arrange
        var instrument = InState(status);

        // Act
        var isActive = instrument.IsActive;

        // Assert
        Assert.Equal(expectedActive, isActive);
    }

    [Fact]
    public void Every_transition_advances_the_updated_stamp()
    {
        // Arrange
        var instrument = Register();
        var listedAt = Now.AddDays(1);

        // Act
        instrument.List(ListingDate, listedAt);

        // Assert
        Assert.Equal(Now, instrument.CreatedAtUtc);
        Assert.Equal(listedAt, instrument.UpdatedAtUtc);
    }

    private static Instrument Register() =>
        Instrument.Register(
            ExchangeId.New(),
            Ticker.Create("FPT"),
            "A Listed Company",
            AssetType.Equity,
            CurrencyCode.Vnd,
            Now);

    private static Instrument Listed()
    {
        var instrument = Register();
        instrument.List(ListingDate, Now.AddDays(1));
        return instrument;
    }

    private static Instrument Delisted()
    {
        var instrument = Listed();
        instrument.Delist(new DateOnly(2026, 9, 1), Now.AddDays(2));
        return instrument;
    }

    private static Instrument InState(InstrumentStatus status) => status switch
    {
        InstrumentStatus.Pending => Register(),
        InstrumentStatus.Listed => Listed(),
        InstrumentStatus.Suspended => Suspended(),
        InstrumentStatus.Delisted => Delisted(),
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static Instrument Suspended()
    {
        var instrument = Listed();
        instrument.Suspend(Now.AddDays(2));
        return instrument;
    }
}
