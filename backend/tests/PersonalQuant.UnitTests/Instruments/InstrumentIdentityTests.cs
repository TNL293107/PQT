using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.UnitTests.Instruments;

/// <summary>
/// Covers the rule the whole instrument master exists for: identity survives
/// every attribute change, because prices, fundamentals and positions join on
/// it.
/// </summary>
public sealed class InstrumentIdentityTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly ListingDate = new(2026, 8, 20);

    [Fact]
    public void Registering_issues_a_canonical_identifier()
    {
        // Act
        var instrument = Register();

        // Assert
        Assert.False(instrument.Id.IsEmpty);
    }

    [Fact]
    public void Two_instruments_never_share_an_identifier()
    {
        // Act
        var first = Register();
        var second = Register();

        // Assert
        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public void Identifiers_are_time_ordered()
    {
        // Version 7 UUIDs embed a timestamp, which keeps the primary key of an
        // append-only table from fragmenting.
        //
        // Compared in big-endian byte order because that is how PostgreSQL
        // orders the uuid type. Guid.CompareTo uses .NET field order and would
        // not prove the property the index actually depends on. The pause
        // crosses a millisecond: within one tick the remaining bits are random
        // and ordering is not guaranteed.
        // Arrange
        var first = InstrumentId.New();
        Thread.Sleep(2);
        var second = InstrumentId.New();

        // Act
        var comparison = first.Value.ToByteArray(bigEndian: true).AsSpan()
            .SequenceCompareTo(second.Value.ToByteArray(bigEndian: true));

        // Assert
        Assert.True(comparison < 0);
    }

    [Fact]
    public void Changing_the_ticker_preserves_identity()
    {
        // The entire reason the ticker is not the key.
        // Arrange
        var instrument = Listed();
        var original = instrument.Id;

        // Act
        instrument.ChangeTicker(Ticker.Create("FPT2"), Now.AddDays(2));

        // Assert
        Assert.Equal(original, instrument.Id);
        Assert.Equal("FPT2", instrument.Ticker.Value);
    }

    [Fact]
    public void Transferring_exchange_preserves_identity()
    {
        // UPCOM to HNX to HOSE is a normal progression for a Vietnamese
        // issuer, and it must not create a second instrument.
        // Arrange
        var instrument = Listed();
        var original = instrument.Id;
        var destination = ExchangeId.New();

        // Act
        instrument.TransferToExchange(destination, Now.AddDays(2));

        // Assert
        Assert.Equal(original, instrument.Id);
        Assert.Equal(destination, instrument.ExchangeId);
    }

    [Fact]
    public void A_delisted_instrument_cannot_change_ticker()
    {
        // Its history is closed; a later edit would rewrite the past.
        // Arrange
        var instrument = Delisted();

        // Act + Assert
        Assert.Throws<DomainStateException>(() =>
            instrument.ChangeTicker(Ticker.Create("XYZ"), Now.AddDays(10)));
    }

    [Fact]
    public void A_delisted_instrument_cannot_transfer_exchange()
    {
        // Arrange
        var instrument = Delisted();

        // Act + Assert
        Assert.Throws<DomainStateException>(() =>
            instrument.TransferToExchange(ExchangeId.New(), Now.AddDays(10)));
    }

    [Fact]
    public void Registering_requires_an_exchange()
    {
        // Act + Assert
        Assert.Throws<DomainValidationException>(() =>
            Instrument.Register(
                default,
                Ticker.Create("FPT"),
                "A Listed Company",
                AssetType.Equity,
                CurrencyCode.Vnd,
                Now));
    }

    [Fact]
    public void Transferring_requires_an_exchange()
    {
        // Arrange
        var instrument = Listed();

        // Act + Assert
        Assert.Throws<DomainValidationException>(() =>
            instrument.TransferToExchange(default, Now.AddDays(2)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Registering_requires_a_name(string name)
    {
        // Act + Assert
        Assert.Throws<DomainValidationException>(() =>
            Instrument.Register(
                ExchangeId.New(),
                Ticker.Create("FPT"),
                name,
                AssetType.Equity,
                CurrencyCode.Vnd,
                Now));
    }

    [Fact]
    public void Registering_rejects_a_name_beyond_the_maximum_length()
    {
        // Arrange
        var tooLong = new string('A', Instrument.MaxNameLength + 1);

        // Act + Assert
        Assert.Throws<DomainValidationException>(() =>
            Instrument.Register(
                ExchangeId.New(),
                Ticker.Create("FPT"),
                tooLong,
                AssetType.Equity,
                CurrencyCode.Vnd,
                Now));
    }

    [Fact]
    public void An_unclassified_instrument_can_be_classified_later()
    {
        // A provider import may not carry an asset type.
        // Arrange
        var instrument = Instrument.Register(
            ExchangeId.New(),
            Ticker.Create("FPT"),
            "A Listed Company",
            AssetType.Unspecified,
            CurrencyCode.Vnd,
            Now);

        // Act
        instrument.Reclassify(AssetType.Equity, Now.AddDays(1));

        // Assert
        Assert.Equal(AssetType.Equity, instrument.AssetType);
    }

    [Fact]
    public void Classification_cannot_be_reverted_to_unspecified()
    {
        // Losing a known classification is never a correction.
        // Arrange
        var instrument = Register();

        // Act + Assert
        Assert.Throws<DomainValidationException>(() =>
            instrument.Reclassify(AssetType.Unspecified, Now.AddDays(1)));
    }

    [Fact]
    public void An_identifier_is_required_before_use()
    {
        // Assert
        Assert.True(default(InstrumentId).IsEmpty);
        Assert.False(InstrumentId.New().IsEmpty);
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
}
