using PersonalQuant.Domain.CorporateActions;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.CorporateActions;

/// <summary>
/// Verifies the arithmetic that rescales history.
/// </summary>
/// <remarks>
/// Every case here is a worked example with the answer computed by hand. The
/// algebra is not hard; what makes it easy to get wrong is the meaning of each
/// type's ratio, and the difference between reading a 10% stock dividend as
/// <c>0.1</c> and as <c>1.1</c> is a series adjusted by a factor of ten.
/// </remarks>
public sealed class AdjustmentFactorsTests
{
    private static readonly DateOnly ExDate = new(2026, 8, 5);
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 1, 0, 0, TimeSpan.Zero);
    private static readonly SourceCode Source = SourceCode.Create("TEST");

    [Fact]
    public void A_two_for_one_split_halves_the_price_and_doubles_the_volume()
    {
        var action = Action(CorporateActionType.StockSplit, ratio: 2m);

        Assert.True(AdjustmentFactors.TryCompute(action, Price.Create(100m), out var factor, out _));

        Assert.Equal(0.5m, factor.Price);
        Assert.Equal(2m, factor.Shares);

        Assert.True(factor.TryApply(Price.Create(100m), out var adjusted));
        Assert.Equal(50m, adjusted.Value);
        Assert.Equal(2_000, factor.ApplyToVolume(1_000));
    }

    [Fact]
    public void A_one_for_ten_consolidation_multiplies_the_price_by_ten()
    {
        // Ratio is shares after per share before, so a reverse split is a
        // fraction. Reading it the other way up inverts the whole series.
        var action = Action(CorporateActionType.ReverseSplit, ratio: 0.1m);

        Assert.True(AdjustmentFactors.TryCompute(action, Price.Create(1_000m), out var factor, out _));

        Assert.Equal(10m, factor.Price);
        Assert.Equal(0.1m, factor.Shares);
        Assert.Equal(100, factor.ApplyToVolume(1_000));
    }

    [Fact]
    public void A_ten_percent_stock_dividend_divides_by_one_point_one()
    {
        // The ratio is additional shares per share held, so 0.1 means eleven
        // shares where there were ten.
        var action = Action(CorporateActionType.StockDividend, ratio: 0.1m);

        Assert.True(AdjustmentFactors.TryCompute(action, Price.Create(110m), out var factor, out _));

        Assert.Equal(1.1m, factor.Shares);
        Assert.True(factor.TryApply(Price.Create(110m), out var adjusted));
        Assert.Equal(100m, adjusted.Value);
    }

    [Fact]
    public void A_bonus_issue_behaves_exactly_like_a_stock_dividend()
    {
        // Arithmetically identical, kept separate because the two are distinct
        // events that reconcile against different announcements.
        var stock = Action(CorporateActionType.StockDividend, ratio: 0.2m);
        var bonus = Action(CorporateActionType.BonusShares, ratio: 0.2m);

        Assert.True(AdjustmentFactors.TryCompute(stock, Price.Create(120m), out var first, out _));
        Assert.True(AdjustmentFactors.TryCompute(bonus, Price.Create(120m), out var second, out _));

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_cash_dividend_lowers_the_price_and_leaves_the_volume_alone()
    {
        // The share count does not change, so adjusting volume for a dividend
        // would invent trading that never happened.
        var action = Action(CorporateActionType.CashDividend, cashAmount: 5m);

        Assert.True(AdjustmentFactors.TryCompute(action, Price.Create(100m), out var factor, out _));

        Assert.Equal(0.95m, factor.Price);
        Assert.Equal(1m, factor.Shares);
        Assert.Equal(1_000, factor.ApplyToVolume(1_000));
    }

    [Fact]
    public void A_dividend_at_or_above_the_previous_close_is_refused()
    {
        // The arithmetic would produce a factor of zero or a negative one. The
        // real explanation is a dividend recorded in dong against a price in
        // thousands, which Vietnamese data invites.
        var action = Action(CorporateActionType.CashDividend, cashAmount: 100m);

        Assert.False(
            AdjustmentFactors.TryCompute(action, Price.Create(100m), out _, out var problem));
        Assert.Contains("different units", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rights_issue_uses_the_theoretical_ex_rights_price()
    {
        // One new share per two held at 10,000 against a close of 40,000:
        // TERP = (40000 + 0.5 × 10000) / 1.5 = 30,000, so the factor is 0.75.
        var action = Action(CorporateActionType.RightsIssue, ratio: 0.5m, cashAmount: 10_000m);

        Assert.True(
            AdjustmentFactors.TryCompute(action, Price.Create(40_000m), out var factor, out _));

        Assert.Equal(0.75m, factor.Price);
        Assert.Equal(1.5m, factor.Shares);
    }

    [Fact]
    public void A_rights_issue_at_a_deeper_discount_moves_the_price_further()
    {
        // The reason the subscription price cannot be ignored. Treating this as
        // a bonus issue of the same ratio would give 1/1.5 = 0.667 for both.
        var shallow = Action(CorporateActionType.RightsIssue, ratio: 0.5m, cashAmount: 30_000m);
        var deep = Action(CorporateActionType.RightsIssue, ratio: 0.5m, cashAmount: 5_000m);

        Assert.True(AdjustmentFactors.TryCompute(shallow, Price.Create(40_000m), out var a, out _));
        Assert.True(AdjustmentFactors.TryCompute(deep, Price.Create(40_000m), out var b, out _));

        Assert.True(b.Price < a.Price);
        Assert.Equal(a.Shares, b.Shares);
    }

    [Fact]
    public void A_subscription_price_at_or_above_the_market_is_refused()
    {
        // Nobody would take up rights priced above the market, so the record is
        // wrong rather than the offer being unattractive.
        var action = Action(CorporateActionType.RightsIssue, ratio: 0.5m, cashAmount: 40_000m);

        Assert.False(
            AdjustmentFactors.TryCompute(action, Price.Create(40_000m), out _, out var problem));
        Assert.Contains("discount", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CorporateActionType.ShareIssuance)]
    [InlineData(CorporateActionType.SymbolChange)]
    public void An_action_that_does_not_rescale_prices_yields_no_factor(CorporateActionType type)
    {
        var action = Action(type);

        Assert.False(
            AdjustmentFactors.TryCompute(action, Price.Create(100m), out _, out var problem));
        Assert.Contains("does not rescale", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cancelled_action_yields_no_factor()
    {
        var action = Action(CorporateActionType.StockSplit, ratio: 2m);
        action.Cancel("The issuer withdrew it.", Now);

        Assert.False(
            AdjustmentFactors.TryCompute(action, Price.Create(100m), out _, out var problem));
        Assert.Contains("cancelled", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_actions_on_one_day_combine_by_multiplication()
    {
        // A cash dividend and a stock dividend on the same ex-date, which
        // Vietnamese issuers pair routinely.
        var cash = Action(CorporateActionType.CashDividend, cashAmount: 2_000m);
        var stock = Action(CorporateActionType.StockDividend, ratio: 0.1m);

        Assert.True(AdjustmentFactors.TryCompute(cash, Price.Create(20_000m), out var first, out _));
        Assert.True(AdjustmentFactors.TryCompute(stock, Price.Create(20_000m), out var second, out _));

        // 0.9 × (1/1.1) = 0.8181818182 to ten places.
        var combined = first.Combine(second);

        Assert.Equal(0.8181818182m, combined.Price);
        Assert.Equal(1.1m, combined.Shares);
    }

    [Fact]
    public void Combining_is_order_independent()
    {
        // What makes a day carrying two actions produce one series whichever
        // order they are applied in.
        var cash = Action(CorporateActionType.CashDividend, cashAmount: 2_000m);
        var split = Action(CorporateActionType.StockSplit, ratio: 2m);

        Assert.True(AdjustmentFactors.TryCompute(cash, Price.Create(20_000m), out var a, out _));
        Assert.True(AdjustmentFactors.TryCompute(split, Price.Create(20_000m), out var b, out _));

        Assert.Equal(a.Combine(b), b.Combine(a));
    }

    [Fact]
    public void An_identity_factor_changes_nothing()
    {
        Assert.True(AdjustmentFactor.Identity.IsIdentity);
        Assert.True(AdjustmentFactor.Identity.TryApply(Price.Create(100m), out var adjusted));
        Assert.Equal(100m, adjusted.Value);
        Assert.Equal(1_000, AdjustmentFactor.Identity.ApplyToVolume(1_000));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-0.5, 1)]
    public void A_factor_that_is_not_positive_is_refused(decimal price, decimal shares) =>
        // Zero would erase the history it was meant to rescale.
        Assert.False(AdjustmentFactor.TryCreate(price, shares, out _));

    private static CorporateAction Action(
        CorporateActionType type,
        decimal? ratio = null,
        decimal? cashAmount = null) =>
        CorporateAction.Record(
            InstrumentId.New(), type, ExDate, ratio, cashAmount, Source, Now);
}
