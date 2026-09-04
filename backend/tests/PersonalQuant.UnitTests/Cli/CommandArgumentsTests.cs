using PersonalQuant.Cli.CommandLine;

namespace PersonalQuant.UnitTests.Cli;

/// <summary>
/// Verifies how a command line is read, and what it refuses.
/// </summary>
/// <remarks>
/// The refusals carry most of the weight here. An operator command that ignores
/// what it did not understand runs over a range nobody asked for and reports
/// success, and the audit trail it leaves is indistinguishable from a correct
/// run.
/// </remarks>
public sealed class CommandArgumentsTests
{
    [Fact]
    public void A_group_a_verb_and_an_option_are_read_apart()
    {
        Assert.True(
            CommandArguments.TryParse(
                ["ingest", "run", "--instrument", "FPT"], out var command, out var problem),
            problem);

        Assert.Equal("ingest", command.Group);
        Assert.Equal("run", command.Verb);
        Assert.Empty(command.Operands);
        Assert.Equal("FPT", command.Value("instrument"));
    }

    [Fact]
    public void A_value_before_the_first_option_is_an_operand()
    {
        Assert.True(
            CommandArguments.TryParse(
                ["provider", "check", "VCI", "--instrument", "FPT"],
                out var command,
                out var problem),
            problem);

        Assert.Equal("VCI", Assert.Single(command.Operands));
    }

    [Fact]
    public void An_option_followed_by_another_option_is_a_flag()
    {
        Assert.True(
            CommandArguments.TryParse(
                ["quality", "resolve", "--explained", "--reason", "moved by decree"],
                out var command,
                out var problem),
            problem);

        Assert.True(command.HasFlag("explained"));
        Assert.Null(command.Value("explained"));
        Assert.Equal("moved by decree", command.Value("reason"));
    }

    [Fact]
    public void A_trailing_option_with_no_value_is_a_flag()
    {
        Assert.True(
            CommandArguments.TryParse(
                ["quality", "resolve", "--dismissed"], out var command, out var problem),
            problem);

        Assert.True(command.HasFlag("dismissed"));
    }

    [Fact]
    public void An_unrecognised_option_is_refused_and_the_accepted_ones_are_named()
    {
        // The defect this exists for is a typo: --form silently ignored is a
        // backfill over the wrong range that reports success.
        Assert.True(
            CommandArguments.TryParse(
                ["ingest", "run", "--form", "2015-01-01"], out var command, out var parse),
            parse);

        Assert.False(command.Validate(["from", "instrument"], out var problem));
        Assert.Contains("--form", problem, StringComparison.Ordinal);
        Assert.Contains("--from", problem, StringComparison.Ordinal);
        Assert.Contains("--instrument", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_recognised_option_set_validates()
    {
        Assert.True(
            CommandArguments.TryParse(
                ["ingest", "run", "--from", "2015-01-01"], out var command, out var parse),
            parse);

        Assert.True(command.Validate(["from", "to"], out var problem), problem);
        Assert.Null(problem);
    }

    [Fact]
    public void The_same_option_twice_is_refused()
    {
        // Taking the last would silently discard the first, and taking the
        // first would silently discard what the operator most recently typed.
        Assert.False(
            CommandArguments.TryParse(
                ["ingest", "run", "--from", "2015-01-01", "--from", "2016-01-01"],
                out _,
                out var problem));

        Assert.Contains("--from", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_value_after_an_option_belongs_to_no_option_and_is_refused()
    {
        Assert.False(
            CommandArguments.TryParse(
                ["provider", "check", "--instrument", "FPT", "VCI"], out _, out var problem));

        Assert.Contains("VCI", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("--help")]
    public void A_line_with_no_verb_is_refused(string token)
    {
        Assert.False(CommandArguments.TryParse([token], out _, out var problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void An_empty_line_is_refused()
    {
        Assert.False(CommandArguments.TryParse([], out _, out var problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void An_option_where_the_group_belongs_is_refused()
    {
        Assert.False(CommandArguments.TryParse(["--instrument", "FPT"], out _, out var problem));
        Assert.Contains("--instrument", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_absent_date_is_absent_rather_than_wrong()
    {
        var command = Parse(["ingest", "run", "--instrument", "FPT"]);

        Assert.True(command.TryDate("from", out var date, out var problem), problem);
        Assert.Null(date);
    }

    [Fact]
    public void A_date_is_read_as_iso_8601()
    {
        var command = Parse(["ingest", "run", "--from", "2021-12-27"]);

        Assert.True(command.TryDate("from", out var date, out var problem), problem);
        Assert.Equal(new DateOnly(2021, 12, 27), date);
    }

    [Theory]
    [InlineData("27/12/2021")]
    [InlineData("12/27/2021")]
    [InlineData("2021-13-01")]
    [InlineData("yesterday")]
    public void A_date_in_any_other_shape_is_refused(string value)
    {
        // Locale-sensitive parsing is how 03/09 becomes March in one shell and
        // September in another, and the backfill covers a different half-year
        // depending on where the operator was sitting.
        var command = Parse(["ingest", "run", "--from", value]);

        Assert.False(command.TryDate("from", out _, out var problem));
        Assert.Contains("yyyy-MM-dd", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_date_option_with_no_date_is_refused_rather_than_treated_as_absent()
    {
        var command = Parse(["ingest", "backfill", "--from", "--instrument"]);

        Assert.False(command.TryDate("from", out _, out var problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void An_absent_count_takes_the_fallback()
    {
        var command = Parse(["quality", "list", "--instrument", "FPT"]);

        Assert.True(command.TryCount("limit", 50, out var limit, out var problem), problem);
        Assert.Equal(50, limit);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("all")]
    public void A_count_that_is_not_a_positive_number_is_refused(string value)
    {
        var command = Parse(["quality", "list", "--limit", value]);

        Assert.False(command.TryCount("limit", 50, out _, out var problem));
        Assert.NotNull(problem);
    }

    [Fact]
    public void A_required_option_that_is_missing_names_the_command_that_needs_it()
    {
        var command = Parse(["ingest", "run", "--interval", "1d"]);

        Assert.False(command.TryRequired("instrument", out _, out var problem));
        Assert.Contains("ingest run", problem, StringComparison.Ordinal);
        Assert.Contains("--instrument", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_required_option_given_as_a_bare_flag_is_refused()
    {
        var command = Parse(["ingest", "run", "--instrument", "--interval"]);

        Assert.False(command.TryRequired("instrument", out _, out var problem));
        Assert.Contains("without a value", problem, StringComparison.Ordinal);
    }

    private static CommandArguments Parse(string[] args)
    {
        Assert.True(CommandArguments.TryParse(args, out var command, out var problem), problem);

        return command;
    }
}
