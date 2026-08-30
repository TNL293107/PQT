using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.UnitTests.MarketData.Fakes;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies which source is chosen for a request, and why none is.
/// </summary>
/// <remarks>
/// <para>
/// The rule under test is <em>exactly one registered source can serve this
/// request</em>, not <em>exactly one source is registered</em>. Until now the
/// second was enough, because there was only ever one; a second provider is
/// what U3 adds, and it is the point at which registration order would
/// otherwise start deciding which vendor a series is attributed to.
/// </para>
/// <para>
/// Half of these tests are about the <em>reason</em> rather than the outcome.
/// The reason lands in the ingestion run that explains a gap in a series, and a
/// vague one there is a gap nobody can close.
/// </para>
/// </remarks>
public sealed class ProviderSelectionTests
{
    private static readonly ExchangeCode Hose = ExchangeCode.Create("HOSE");
    private static readonly ExchangeCode Upcom = ExchangeCode.Create("UPCOM");

    [Fact]
    public void One_registered_source_that_serves_the_request_is_chosen()
    {
        // The case every seam in the pipeline was built against. It must keep
        // behaving exactly as it did.
        var registry = Registry(Provider("ONE"));

        var selection = registry.SelectProvider(new ProviderCriteria(BarInterval.OneDay));

        Assert.Equal(ProviderSelectionOutcome.Selected, selection.Outcome);
        Assert.Equal("ONE", selection.Provider?.Code.Value);
    }

    [Fact]
    public void With_two_sources_only_one_of_which_can_serve_it_that_one_is_chosen()
    {
        // A daily Vietnamese feed beside an intraday-only feed is not an
        // ambiguity about a daily request. Under the old rule it was an error.
        var registry = Registry(
            Provider("DAILY", intervals: Set(BarInterval.OneDay)),
            Provider("INTRA", intervals: Set(BarInterval.OneMinute)));

        var selection = registry.SelectProvider(new ProviderCriteria(BarInterval.OneDay));

        Assert.Equal(ProviderSelectionOutcome.Selected, selection.Outcome);
        Assert.Equal("DAILY", selection.Provider?.Code.Value);
    }

    [Fact]
    public void Two_sources_that_can_both_serve_it_are_ambiguous_and_neither_is_chosen()
    {
        // Two answers to one question. Choosing by registration order would
        // attribute the series to whichever was composed first, and nothing
        // downstream would record that a choice had been made at all.
        var registry = Registry(Provider("ALPHA"), Provider("BETA"));

        var selection = registry.SelectProvider(new ProviderCriteria(BarInterval.OneDay));

        Assert.Equal(ProviderSelectionOutcome.Ambiguous, selection.Outcome);
        Assert.Null(selection.Provider);
        Assert.Contains("ALPHA", selection.Reason, StringComparison.Ordinal);
        Assert.Contains("BETA", selection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_source_that_is_not_registered_is_unknown_and_named_in_the_reason()
    {
        var registry = Registry(Provider("ONE"));

        var selection = registry.SelectProvider(
            new ProviderCriteria(BarInterval.OneDay, SourceCode.Create("ABSENT")));

        Assert.Equal(ProviderSelectionOutcome.Unknown, selection.Outcome);
        Assert.Contains("ABSENT", selection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_source_that_does_not_serve_the_interval_names_the_interval()
    {
        var registry = Registry(Provider("DAILY", intervals: Set(BarInterval.OneDay)));

        var selection = registry.SelectProvider(
            new ProviderCriteria(BarInterval.OneMinute, SourceCode.Create("DAILY")));

        Assert.Equal(ProviderSelectionOutcome.Incapable, selection.Outcome);
        Assert.Contains("DAILY", selection.Reason, StringComparison.Ordinal);
        Assert.Contains("OneMinute", selection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_source_that_does_not_cover_the_venue_names_the_venue()
    {
        var registry = Registry(Provider("HOSEONLY", exchanges: Set(Hose)));

        var selection = registry.SelectProvider(new ProviderCriteria(
            BarInterval.OneDay,
            SourceCode.Create("HOSEONLY"),
            Upcom));

        Assert.Equal(ProviderSelectionOutcome.Incapable, selection.Outcome);
        Assert.Contains("UPCOM", selection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_named_source_that_does_not_serve_the_asset_type_names_the_type()
    {
        var registry = Registry(Provider("EQUITY", assetTypes: Set(AssetType.Equity)));

        var selection = registry.SelectProvider(new ProviderCriteria(
            BarInterval.OneDay,
            SourceCode.Create("EQUITY"),
            Hose,
            AssetType.Index));

        Assert.Equal(ProviderSelectionOutcome.Incapable, selection.Outcome);
        Assert.Contains("Index", selection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_restriction_set_is_no_restriction()
    {
        // Correct for a directory of CSV files, and wrong for a vendor — which
        // is why a vendor declares its venues rather than leaving them empty.
        var registry = Registry(Provider("ANY"));

        var selection = registry.SelectProvider(
            new ProviderCriteria(BarInterval.OneDay, Exchange: Upcom, AssetType: AssetType.Index));

        Assert.Equal(ProviderSelectionOutcome.Selected, selection.Outcome);
    }

    [Fact]
    public void An_unknown_venue_cannot_refuse_a_source()
    {
        // Selection happens for instructions naming an instrument this system
        // does not hold. Not knowing the venue is not evidence that the source
        // fails to cover it, so the dimension is simply not tested.
        var registry = Registry(Provider("HOSEONLY", exchanges: Set(Hose)));

        var selection = registry.SelectProvider(new ProviderCriteria(BarInterval.OneDay));

        Assert.Equal(ProviderSelectionOutcome.Selected, selection.Outcome);
    }

    [Fact]
    public void A_single_source_that_cannot_serve_the_request_says_which_dimension_failed()
    {
        // Not "nothing matched". With one source registered there is exactly
        // one thing to explain, and the run's reason is the only record of why
        // a series has a gap.
        var registry = Registry(Provider("DAILY", intervals: Set(BarInterval.OneDay)));

        var selection = registry.SelectProvider(new ProviderCriteria(BarInterval.OneMinute));

        Assert.Equal(ProviderSelectionOutcome.Incapable, selection.Outcome);
        Assert.Contains("DAILY", selection.Reason, StringComparison.Ordinal);
        Assert.Contains("OneMinute", selection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void With_nothing_registered_the_reason_says_so()
    {
        var registry = new MarketDataProviderRegistry([]);

        var selection = registry.SelectProvider(new ProviderCriteria(BarInterval.OneDay));

        Assert.Equal(ProviderSelectionOutcome.None, selection.Outcome);
        Assert.Contains("No market data source is registered", selection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Several_registered_sources_none_of_which_can_serve_it_are_listed()
    {
        var registry = Registry(
            Provider("AAA", intervals: Set(BarInterval.OneDay)),
            Provider("BBB", intervals: Set(BarInterval.OneDay)));

        var selection = registry.SelectProvider(new ProviderCriteria(BarInterval.FiveMinutes));

        Assert.Equal(ProviderSelectionOutcome.None, selection.Outcome);
        Assert.Contains("AAA", selection.Reason, StringComparison.Ordinal);
        Assert.Contains("BBB", selection.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void There_is_no_selection_that_yields_a_second_source_after_a_first()
    {
        // The property, asserted as a property: whatever the criteria, a
        // selection returns one source or none. Nothing in this type can
        // produce an ordered list of candidates to try in turn, which is what
        // a fallback would need.
        var registry = Registry(Provider("ALPHA"), Provider("BETA"));

        foreach (var interval in new[] { BarInterval.OneDay, BarInterval.OneMinute })
        {
            var selection = registry.SelectProvider(new ProviderCriteria(interval));

            Assert.False(selection.IsSelected);
            Assert.Equal(ProviderSelectionOutcome.Ambiguous, selection.Outcome);
        }
    }

    [Fact]
    public void A_source_declaring_no_resolutions_fails_at_composition()
    {
        // It could never answer anything. A deployment finds that out at
        // start-up rather than through a night of skipped runs.
        var provider = new ScriptedProvider(SourceCode.Create("EMPTY"), _ => throw new NotSupportedException())
        {
            Capability = TestCapability.For(
                SourceCode.Create("EMPTY"),
                new HashSet<BarInterval>()),
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => new MarketDataProviderRegistry([provider]));

        Assert.Contains("EMPTY", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_capability_declared_for_another_code_fails_at_composition()
    {
        // A copy-paste in a provider declaration would otherwise make one
        // source answer under another's name.
        var provider = new ScriptedProvider(SourceCode.Create("MINE"), _ => throw new NotSupportedException())
        {
            Capability = TestCapability.For(SourceCode.Create("YOURS")),
        };

        var error = Assert.Throws<InvalidOperationException>(
            () => new MarketDataProviderRegistry([provider]));

        Assert.Contains("MINE", error.Message, StringComparison.Ordinal);
        Assert.Contains("YOURS", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unstated_coverage_floor_is_unknown_rather_than_unbounded()
    {
        // The same rule U2 applies to universe coverage. Nothing may read a
        // null floor as "holds everything".
        var capability = TestCapability.For(SourceCode.Create("ONE"));

        Assert.Null(capability.EarliestAvailable);
    }

    [Fact]
    public void Supported_intervals_are_the_declared_intervals()
    {
        // One property, derived, so the two can never disagree.
        IMarketDataProvider provider = Provider("DAILY", intervals: Set(BarInterval.OneDay));

        Assert.Equal(provider.Capability.Intervals, provider.SupportedIntervals);
    }

    private static HashSet<T> Set<T>(params T[] values) => [.. values];

    private static MarketDataProviderRegistry Registry(params IMarketDataProvider[] providers) =>
        new(providers);

    private static ScriptedProvider Provider(
        string code,
        IReadOnlySet<BarInterval>? intervals = null,
        IReadOnlySet<ExchangeCode>? exchanges = null,
        IReadOnlySet<AssetType>? assetTypes = null)
    {
        var source = SourceCode.Create(code);

        return new ScriptedProvider(source, _ => throw new NotSupportedException())
        {
            Capability = TestCapability.For(source, intervals, exchanges, assetTypes),
        };
    }
}
