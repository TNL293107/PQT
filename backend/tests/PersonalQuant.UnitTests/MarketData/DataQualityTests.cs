using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies the finding's lifecycle, the score's arithmetic, the calendar
/// window, and the lineage a bar carries.
/// </summary>
public sealed class DataQualityTests
{
    private static readonly DateTimeOffset Session = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_raised_finding_starts_open_and_records_the_rules_that_found_it()
    {
        var issue = Raise();

        Assert.True(issue.IsOpen);
        Assert.Equal(DataQualityIssueStatus.Open, issue.Status);
        Assert.Equal(DataRules.ValidationVersion, issue.ValidationVersion);
        Assert.Null(issue.ResolvedAtUtc);
        Assert.Null(issue.Resolution);
    }

    [Fact]
    public void A_finding_raised_by_no_rules_is_rejected() =>
        // It could never be re-evaluated when the rules change, which is most
        // of what the version is for.
        Assert.Throws<DomainValidationException>(() => DataQualityIssue.Raise(
            InstrumentId.New(),
            BarInterval.OneDay,
            Session,
            DataQualityIssueKind.MissingSession,
            "detail",
            DataRules.Unvalidated,
            Now));

    [Fact]
    public void A_finding_off_a_period_boundary_is_rejected() =>
        Assert.Throws<DomainValidationException>(() => DataQualityIssue.Raise(
            InstrumentId.New(),
            BarInterval.OneDay,
            Session.AddHours(2),
            DataQualityIssueKind.MissingSession,
            "detail",
            DataRules.ValidationVersion,
            Now));

    [Fact]
    public void Explaining_a_finding_closes_it_with_what_accounted_for_it()
    {
        var issue = Raise();

        // Act
        issue.Explain("A two-for-one split with an ex-date of 2026-08-05.", Now);

        // Assert
        Assert.False(issue.IsOpen);
        Assert.Equal(DataQualityIssueStatus.Explained, issue.Status);
        Assert.Equal(Now, issue.ResolvedAtUtc);
        Assert.Contains("split", issue.Resolution, StringComparison.Ordinal);
    }

    [Fact]
    public void Dismissing_a_finding_closes_it_as_not_a_problem()
    {
        var issue = Raise();

        issue.Dismiss("The venue confirmed the print.", Now);

        Assert.Equal(DataQualityIssueStatus.Dismissed, issue.Status);
    }

    [Fact]
    public void A_closed_finding_cannot_be_resolved_again()
    {
        // Overwriting a resolution would erase the audit trail the finding
        // exists to leave.
        var issue = Raise();
        issue.Dismiss("Benign.", Now);

        Assert.Throws<DomainStateException>(() => issue.Explain("Actually a split.", Now));
    }

    [Fact]
    public void A_resolution_must_say_something()
    {
        var issue = Raise();

        Assert.Throws<DomainValidationException>(() => issue.Explain("   ", Now));
    }

    [Fact]
    public void The_score_weights_completeness_heaviest()
    {
        // A missing session is the failure research cannot work around.
        var missingSessions = DataQualityScore.From(0.5m, 1m, 1m, 1m);
        var unreliableSource = DataQualityScore.From(1m, 1m, 1m, 0.5m);

        Assert.True(missingSessions.Overall < unreliableSource.Overall);
    }

    [Fact]
    public void The_weights_sum_to_one()
    {
        // Otherwise a perfect series would not score one.
        Assert.Equal(
            1m,
            DataQualityScore.CompletenessWeight
            + DataQualityScore.ConsistencyWeight
            + DataQualityScore.ValidityWeight
            + DataQualityScore.SourceReliabilityWeight);

        Assert.Equal(1m, DataQualityScore.From(1m, 1m, 1m, 1m).Overall);
    }

    [Fact]
    public void Components_are_bounded_to_the_unit_interval()
    {
        var score = DataQualityScore.From(1.5m, -0.2m, 2m, 0.5m);

        Assert.Equal(1m, score.Completeness);
        Assert.Equal(0m, score.Consistency);
        Assert.Equal(1m, score.Validity);
    }

    [Fact]
    public void A_ratio_with_no_denominator_reports_nothing_known_to_be_wrong()
    {
        // Zero would report a series nobody has ingested yet as maximally
        // broken. The counts travel beside the score so the difference stays
        // visible.
        Assert.Equal(1m, DataQualityScore.Ratio(0, 0));
        Assert.Equal(0m, DataQualityScore.Ratio(0, 10));
        Assert.Equal(0.5m, DataQualityScore.Ratio(5, 10));
    }

    [Fact]
    public void A_calendar_window_excludes_weekends_without_recording_them()
    {
        var window = new TradingCalendarWindow(
            ExchangeId.New(),
            new DateOnly(2026, 8, 3),
            new DateOnly(2026, 8, 9),
            new HashSet<DateOnly>(),
            IsComplete: true);

        Assert.Equal(5, window.TradingDays().Count());
        Assert.False(window.IsTradingDay(new DateOnly(2026, 8, 8)));
        Assert.False(window.IsTradingDay(new DateOnly(2026, 8, 9)));
        Assert.True(window.IsTradingDay(new DateOnly(2026, 8, 3)));
    }

    [Fact]
    public void A_recorded_closure_is_not_a_trading_day()
    {
        var window = new TradingCalendarWindow(
            ExchangeId.New(),
            new DateOnly(2026, 8, 3),
            new DateOnly(2026, 8, 7),
            new HashSet<DateOnly> { new(2026, 8, 5) },
            IsComplete: true);

        Assert.Equal(4, window.TradingDays().Count());
        Assert.False(window.IsTradingDay(new DateOnly(2026, 8, 5)));
    }

    [Fact]
    public void A_new_bar_records_its_transformation_rules_and_no_validation()
    {
        // Validation happens after the bar exists, so claiming it at
        // construction would assert a check that has not run.
        var bar = Bar();

        Assert.Equal(DataRules.TransformationVersion, bar.TransformationVersion);
        Assert.Equal(DataRules.Unvalidated, bar.ValidationVersion);
    }

    [Fact]
    public void Marking_a_bar_validated_records_the_rule_version()
    {
        var bar = Bar();

        bar.MarkValidated(DataRules.ValidationVersion);

        Assert.Equal(DataRules.ValidationVersion, bar.ValidationVersion);
    }

    [Fact]
    public void A_restated_bar_loses_its_validation_stamp()
    {
        // The values moved, so whatever the rules concluded about the old ones
        // no longer applies.
        var bar = Bar();
        bar.MarkValidated(DataRules.ValidationVersion);

        // Act
        var changed = bar.Revise(
            Price.Create(100m),
            Price.Create(120m),
            Price.Create(90m),
            Price.Create(110m),
            2_000,
            null,
            SourceCode.Create("TEST"),
            Now.AddDays(1));

        // Assert
        Assert.True(changed);
        Assert.Equal(DataRules.Unvalidated, bar.ValidationVersion);
        Assert.Equal(DataRules.TransformationVersion, bar.TransformationVersion);
    }

    [Fact]
    public void An_unchanged_restatement_leaves_the_validation_stamp_alone()
    {
        // Re-fetching a range that has not moved is the normal case, and it
        // must not force a re-check of the whole window.
        var bar = Bar();
        bar.MarkValidated(DataRules.ValidationVersion);

        var changed = bar.Revise(
            Price.Create(100m),
            Price.Create(110m),
            Price.Create(95m),
            Price.Create(105m),
            1_000,
            null,
            SourceCode.Create("TEST"),
            Now.AddDays(1));

        Assert.False(changed);
        Assert.Equal(DataRules.ValidationVersion, bar.ValidationVersion);
    }

    [Fact]
    public void A_trading_holiday_must_say_what_it_is() =>
        // "Why was the market shut on this date?" is the question the row
        // exists to answer.
        Assert.Throws<DomainValidationException>(() => TradingHoliday.Record(
            ExchangeId.New(), new DateOnly(2026, 9, 2), "   ", Now));

    [Fact]
    public void A_trading_holiday_records_its_venue_date_and_reason()
    {
        var venue = ExchangeId.New();

        var holiday = TradingHoliday.Record(venue, new DateOnly(2026, 9, 2), "National Day", Now);

        Assert.Equal(venue, holiday.ExchangeId);
        Assert.Equal(new DateOnly(2026, 9, 2), holiday.Date);
        Assert.Equal("National Day", holiday.Name);
    }

    private static DataQualityIssue Raise() =>
        DataQualityIssue.Raise(
            InstrumentId.New(),
            BarInterval.OneDay,
            Session,
            DataQualityIssueKind.PriceLimitBreach,
            "The close moved -50% beyond the band.",
            DataRules.ValidationVersion,
            Now);

    private static OhlcvBar Bar() =>
        OhlcvBar.Record(
            InstrumentId.New(),
            BarInterval.OneDay,
            Session,
            Price.Create(100m),
            Price.Create(110m),
            Price.Create(95m),
            Price.Create(105m),
            1_000,
            null,
            SourceCode.Create("TEST"),
            Now);
}
