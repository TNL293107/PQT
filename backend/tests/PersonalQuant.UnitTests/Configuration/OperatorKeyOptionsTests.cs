using System.ComponentModel.DataAnnotations;
using PersonalQuant.Api.Configuration;

namespace PersonalQuant.UnitTests.Configuration;

public sealed class OperatorKeyOptionsTests
{
    [Fact]
    public void No_key_is_valid_and_disables_writes()
    {
        // A fresh clone is read-only until the operator opts in (ADR-027).
        var options = new OperatorKeyOptions();

        Assert.Empty(Validate(options));
        Assert.False(options.WritesEnabled);
    }

    [Fact]
    public void A_key_shorter_than_the_minimum_is_refused()
    {
        var options = new OperatorKeyOptions { OperatorKey = new string('k', OperatorKeyOptions.MinimumKeyLength - 1) };

        Assert.Single(Validate(options));
    }

    [Fact]
    public void A_key_with_surrounding_whitespace_is_refused()
    {
        // It would refuse every request and look like a wrong key.
        var options = new OperatorKeyOptions { OperatorKey = new string('k', 40) + "\n" };

        Assert.Single(Validate(options));
    }

    [Fact]
    public void A_long_enough_key_enables_writes()
    {
        var options = new OperatorKeyOptions { OperatorKey = new string('k', OperatorKeyOptions.MinimumKeyLength) };

        Assert.Empty(Validate(options));
        Assert.True(options.WritesEnabled);
    }

    private static List<ValidationResult> Validate(OperatorKeyOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);

        return results;
    }
}
