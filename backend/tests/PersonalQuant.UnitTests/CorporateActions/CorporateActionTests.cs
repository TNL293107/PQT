using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.CorporateActions;

/// <summary>
/// Verifies what may be recorded as a corporate action.
/// </summary>
/// <remarks>
/// The validation matters more than usual here. A ratio on a cash dividend or a
/// missing subscription price on a rights issue does not produce an obviously
/// broken record — it produces one that rescales a decade of prices by the
/// wrong amount, and nothing downstream says so.
/// </remarks>
public sealed class CorporateActionTests
{
    private static readonly DateOnly ExDate = new(2026, 8, 5);
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 1, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Source = SourceCode.Create("TEST");

    [Fact]
    public void A_recorded_action_starts_at_version_one_and_is_not_cancelled()
    {
        var action = Record(CorporateActionType.StockSplit, ratio: 2m);

        Assert.Equal(1, action.Version);
        Assert.False(action.IsCancelled);
        Assert.True(action.AffectsPrice);
        Assert.Equal(ExDate, action.ExDate);
    }

    [Theory]
    [InlineData(CorporateActionType.StockSplit)]
    [InlineData(CorporateActionType.ReverseSplit)]
    [InlineData(CorporateActionType.StockDividend)]
    [InlineData(CorporateActionType.BonusShares)]
    public void A_type_that_needs_a_ratio_is_refused_without_one(CorporateActionType type) =>
        Assert.Throws<DomainValidationException>(() => Record(type));

    [Fact]
    public void A_cash_dividend_is_refused_without_a_cash_amount() =>
        Assert.Throws<DomainValidationException>(() => Record(CorporateActionType.CashDividend));

    [Fact]
    public void A_rights_issue_needs_both_a_ratio_and_a_subscription_price()
    {
        Assert.Throws<DomainValidationException>(
            () => Record(CorporateActionType.RightsIssue, ratio: 0.5m));
        Assert.Throws<DomainValidationException>(
            () => Record(CorporateActionType.RightsIssue, cashAmount: 10_000m));

        var action = Record(CorporateActionType.RightsIssue, ratio: 0.5m, cashAmount: 10_000m);

        Assert.Equal(0.5m, action.Ratio);
    }

    [Fact]
    public void A_ratio_on_a_type_that_carries_none_is_refused() =>
        // Silently ignoring it would leave a record that reads as though it
        // means something it does not.
        Assert.Throws<DomainValidationException>(
            () => Record(CorporateActionType.CashDividend, ratio: 2m, cashAmount: 500m));

    [Fact]
    public void A_cash_amount_on_a_type_that_carries_none_is_refused() =>
        Assert.Throws<DomainValidationException>(
            () => Record(CorporateActionType.StockSplit, ratio: 2m, cashAmount: 500m));

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public void A_ratio_that_is_not_positive_is_refused(decimal ratio) =>
        Assert.Throws<DomainValidationException>(
            () => Record(CorporateActionType.StockSplit, ratio: ratio));

    [Fact]
    public void A_split_that_changes_nothing_is_refused() =>
        // A factor of exactly one is a transcription error rather than an
        // event.
        Assert.Throws<DomainValidationException>(
            () => Record(CorporateActionType.StockSplit, ratio: 1m));

    [Fact]
    public void A_stock_dividend_of_a_hundred_percent_is_a_real_event()
    {
        // Additional shares per share held, so a ratio of one doubles the
        // count. Unlike a split ratio of one, this is not a no-op.
        var action = Record(CorporateActionType.StockDividend, ratio: 1m);

        Assert.Equal(1m, action.Ratio);
    }

    [Fact]
    public void Scheduling_records_the_dates_around_the_ex_date()
    {
        var action = Record(CorporateActionType.CashDividend, cashAmount: 500m);

        action.Schedule(
            new DateOnly(2026, 8, 6), new DateOnly(2026, 8, 20), new DateOnly(2026, 7, 15), Now);

        Assert.Equal(new DateOnly(2026, 8, 6), action.RecordDate);
        Assert.Equal(new DateOnly(2026, 8, 20), action.PaymentDate);
        Assert.Equal(new DateOnly(2026, 7, 15), action.AnnouncedOn);
    }

    [Fact]
    public void An_announcement_after_the_ex_date_is_refused()
    {
        // Two fields transposed. Accepting it would let a point-in-time read
        // hide an action the market already knew about.
        var action = Record(CorporateActionType.CashDividend, cashAmount: 500m);

        Assert.Throws<DomainValidationException>(
            () => action.Schedule(null, null, ExDate.AddDays(1), Now));
    }

    [Fact]
    public void A_payment_before_the_record_date_is_refused()
    {
        var action = Record(CorporateActionType.CashDividend, cashAmount: 500m);

        Assert.Throws<DomainValidationException>(() => action.Schedule(
            new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 6), null, Now));
    }

    [Fact]
    public void Amending_an_unchanged_action_reports_no_change()
    {
        // A re-import of an action that has not moved must not invalidate a
        // factor that is still correct.
        var action = Record(CorporateActionType.StockSplit, ratio: 2m);

        var changed = action.Amend(ExDate, 2m, null, "Re-imported.", Now);

        Assert.False(changed);
        Assert.Equal(1, action.Version);
    }

    [Fact]
    public void Amending_a_ratio_bumps_the_version()
    {
        var action = Record(CorporateActionType.StockSplit, ratio: 2m);

        var changed = action.Amend(ExDate, 4m, null, "The issuer restated it.", Now);

        Assert.True(changed);
        Assert.Equal(2, action.Version);
        Assert.Equal(4m, action.Ratio);
        Assert.Contains("restated", action.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Cancelling_stops_the_action_rescaling_anything()
    {
        var action = Record(CorporateActionType.StockSplit, ratio: 2m);

        action.Cancel("The issuer withdrew it.", Now);

        Assert.True(action.IsCancelled);
        Assert.False(action.AffectsPrice);
        Assert.Equal(2, action.Version);
    }

    [Fact]
    public void A_cancelled_action_cannot_be_amended()
    {
        var action = Record(CorporateActionType.StockSplit, ratio: 2m);
        action.Cancel("Withdrawn.", Now);

        Assert.Throws<DomainStateException>(() => action.Amend(ExDate, 4m, null, null, Now));
    }

    [Fact]
    public void A_cancelled_action_cannot_be_cancelled_twice()
    {
        var action = Record(CorporateActionType.StockSplit, ratio: 2m);
        action.Cancel("Withdrawn.", Now);

        Assert.Throws<DomainStateException>(() => action.Cancel("Again.", Now));
    }

    [Theory]
    [InlineData(CorporateActionType.ShareIssuance)]
    [InlineData(CorporateActionType.SymbolChange)]
    public void An_action_that_rescales_nothing_needs_neither_amount(CorporateActionType type)
    {
        var action = Record(type);

        Assert.False(action.AffectsPrice);
        Assert.Null(action.Ratio);
        Assert.Null(action.CashAmount);
    }

    [Fact]
    public void An_undeclared_type_is_refused() =>
        Assert.Throws<DomainValidationException>(
            () => Record(CorporateActionType.Unspecified));

    [Fact]
    public void An_action_without_an_instrument_is_refused() =>
        Assert.Throws<DomainValidationException>(() => CorporateAction.Record(
            default, CorporateActionType.SymbolChange, ExDate, null, null, Source, Now));

    [Fact]
    public void An_adjustment_records_the_action_version_it_was_computed_from()
    {
        // What makes a stale factor findable by comparison rather than by
        // re-adjusting everything.
        var action = Record(CorporateActionType.StockSplit, ratio: 2m);

        var adjustment = PriceAdjustment.For(
            action,
            AdjustmentFactor.Create(0.5m, 2m),
            Price.Create(100m),
            DataRules.AdjustmentVersion,
            Now);

        Assert.True(adjustment.IsCurrentFor(action));

        action.Amend(ExDate, 4m, null, "Restated.", Now);

        Assert.False(adjustment.IsCurrentFor(action));
    }

    [Fact]
    public void An_adjustment_that_rescales_nothing_is_refused() =>
        // A stored row multiplying by one is noise every read has to carry.
        Assert.Throws<DomainValidationException>(() => PriceAdjustment.For(
            Record(CorporateActionType.StockSplit, ratio: 2m),
            AdjustmentFactor.Identity,
            Price.Create(100m),
            DataRules.AdjustmentVersion,
            Now));

    [Fact]
    public void An_adjustment_applies_only_to_bars_before_the_ex_date()
    {
        var adjustment = PriceAdjustment.For(
            Record(CorporateActionType.StockSplit, ratio: 2m),
            AdjustmentFactor.Create(0.5m, 2m),
            Price.Create(100m),
            DataRules.AdjustmentVersion,
            Now);

        var exDateUtc = new DateTimeOffset(ExDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        Assert.True(adjustment.AppliesTo(exDateUtc.AddDays(-1)));
        Assert.False(adjustment.AppliesTo(exDateUtc));
        Assert.False(adjustment.AppliesTo(exDateUtc.AddDays(1)));
    }

    private static CorporateAction Record(
        CorporateActionType type,
        decimal? ratio = null,
        decimal? cashAmount = null) =>
        CorporateAction.Record(
            InstrumentId.New(), type, ExDate, ratio, cashAmount, Source, Now);
}
