using PersonalQuant.Application.Instruments;

namespace PersonalQuant.UnitTests.Instruments;

/// <summary>
/// Covers the boundary that decides what reaches the database. Nothing
/// downstream re-validates a criteria instance, so anything this type accepts
/// becomes a query.
/// </summary>
public sealed class InstrumentSearchCriteriaTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_query_is_rejected_with_a_reason(string? text)
    {
        // Act
        var accepted = InstrumentSearchCriteria.TryCreate(
            text, limit: null, includeInactive: false, out var criteria, out var problem);

        // Assert
        Assert.False(accepted);
        Assert.Null(criteria);
        Assert.NotNull(problem);
    }

    [Fact]
    public void A_query_that_folds_to_nothing_is_rejected()
    {
        // Non-empty on the way in, nothing left after folding. Allowing it
        // through would produce a LIKE '%%' scan of the whole table.
        // Act
        var accepted = InstrumentSearchCriteria.TryCreate(
            "́̀", limit: null, includeInactive: false, out var criteria, out var problem);

        // Assert
        Assert.False(accepted);
        Assert.Null(criteria);
        Assert.NotNull(problem);
    }

    [Fact]
    public void An_over_long_query_is_rejected()
    {
        // Arrange
        var tooLong = new string('A', InstrumentSearchCriteria.MaxTextLength + 1);

        // Act
        var accepted = InstrumentSearchCriteria.TryCreate(
            tooLong, limit: null, includeInactive: false, out _, out var problem);

        // Assert
        Assert.False(accepted);
        Assert.NotNull(problem);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(InstrumentSearchCriteria.MaxLimit + 1)]
    public void A_limit_outside_the_permitted_range_is_rejected(int limit)
    {
        // The bound is what stops an anonymous caller asking the database for
        // an arbitrary amount of work on a per-keystroke endpoint.
        // Act
        var accepted = InstrumentSearchCriteria.TryCreate(
            "FPT", limit, includeInactive: false, out _, out var problem);

        // Assert
        Assert.False(accepted);
        Assert.NotNull(problem);
    }

    [Fact]
    public void An_absent_limit_falls_back_to_the_default()
    {
        // Act
        var accepted = InstrumentSearchCriteria.TryCreate(
            "FPT", limit: null, includeInactive: false, out var criteria, out _);

        // Assert
        Assert.True(accepted);
        Assert.NotNull(criteria);
        Assert.Equal(InstrumentSearchCriteria.DefaultLimit, criteria.Limit);
    }

    [Fact]
    public void An_accepted_query_is_stored_folded()
    {
        // The repository matches against folded columns and does no folding of
        // its own, so the folding has to have happened by now.
        // Act
        var accepted = InstrumentSearchCriteria.TryCreate(
            "  ngân hàng  ", limit: 5, includeInactive: false, out var criteria, out _);

        // Assert
        Assert.True(accepted);
        Assert.NotNull(criteria);
        Assert.Equal("NGAN HANG", criteria.Text);
        Assert.Equal(5, criteria.Limit);
    }

    [Fact]
    public void Delisted_instruments_are_excluded_unless_asked_for()
    {
        // A delisted security must not be offerable as a selection by
        // accident: its ticker may already belong to a different issuer.
        // Act
        InstrumentSearchCriteria.TryCreate(
            "FPT", limit: null, includeInactive: false, out var excluded, out _);
        InstrumentSearchCriteria.TryCreate(
            "FPT", limit: null, includeInactive: true, out var included, out _);

        // Assert
        Assert.False(excluded!.IncludeInactive);
        Assert.True(included!.IncludeInactive);
    }
}
