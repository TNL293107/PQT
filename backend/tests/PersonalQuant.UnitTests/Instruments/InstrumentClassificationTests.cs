using PersonalQuant.Domain.Classification;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.UnitTests.Instruments;

/// <summary>
/// Verifies how an instrument records the industry it is classified under.
/// </summary>
/// <remarks>
/// Classification is descriptive metadata rather than identity, so the rules
/// here are deliberately looser than the lifecycle's: it may be set, replaced
/// and removed. What it may not be is silently wrong.
/// </remarks>
public sealed class InstrumentClassificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_newly_registered_instrument_is_unclassified()
    {
        // Nullable rather than a catch-all node: "not mapped yet" and "not in
        // an industry at all" are both real, and neither is a sector.
        var instrument = Register();

        Assert.Null(instrument.IndustryId);
    }

    [Fact]
    public void Assigning_an_industry_records_it_and_advances_the_updated_stamp()
    {
        var instrument = Register();
        var industryId = IndustryId.New();
        var later = Now.AddDays(1);

        // Act
        instrument.AssignIndustry(industryId, later);

        // Assert
        Assert.Equal(industryId, instrument.IndustryId);
        Assert.Equal(later, instrument.UpdatedAtUtc);
    }

    [Fact]
    public void Reassigning_an_industry_replaces_the_previous_one()
    {
        // A provider reclassifying a company is ordinary, so the aggregate
        // records the current answer rather than refusing the change.
        var instrument = Register();
        var first = IndustryId.New();
        var second = IndustryId.New();

        instrument.AssignIndustry(first, Now);

        // Act
        instrument.AssignIndustry(second, Now.AddDays(1));

        // Assert
        Assert.Equal(second, instrument.IndustryId);
    }

    [Fact]
    public void An_unassigned_industry_is_rejected()
    {
        var instrument = Register();

        Assert.Throws<DomainValidationException>(
            () => instrument.AssignIndustry(default, Now));
    }

    [Fact]
    public void Clearing_the_industry_returns_the_instrument_to_unclassified()
    {
        // For a mapping discovered to be wrong. Leaving it in place would keep
        // the security inside a peer group it does not belong to.
        var instrument = Register();
        instrument.AssignIndustry(IndustryId.New(), Now);

        // Act
        instrument.ClearIndustry(Now.AddDays(1));

        // Assert
        Assert.Null(instrument.IndustryId);
    }

    [Fact]
    public void Classification_survives_a_ticker_change_and_an_exchange_transfer()
    {
        // The whole point of an internal identity: the security is the same
        // company after both, so its industry is unchanged.
        var instrument = Register();
        var industryId = IndustryId.New();
        instrument.AssignIndustry(industryId, Now);
        instrument.List(Now);

        // Act
        instrument.ChangeTicker(Ticker.Create("FPT2"), Now.AddDays(1));
        instrument.TransferToExchange(ExchangeId.New(), Now.AddDays(2));

        // Assert
        Assert.Equal(industryId, instrument.IndustryId);
    }

    private static Instrument Register() =>
        Instrument.Register(
            ExchangeId.New(),
            Ticker.Create("FPT"),
            "FPT Corporation",
            AssetType.Equity,
            CurrencyCode.Vnd,
            Now);
}
