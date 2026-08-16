using PersonalQuant.Domain.Classification;
using PersonalQuant.Domain.Common;

namespace PersonalQuant.UnitTests.Classification;

/// <summary>
/// Verifies the parsing rules of <see cref="ClassificationCode"/>.
/// </summary>
/// <remarks>
/// The code is what a provider mapping and the seed file both key on, so two
/// spellings of one node must not be able to become two rows.
/// </remarks>
public sealed class ClassificationCodeTests
{
    [Theory]
    [InlineData("tech", "TECH")]
    [InlineData("  fin-bank  ", "FIN-BANK")]
    [InlineData("Consstap", "CONSSTAP")]
    public void Creating_a_code_upper_cases_it_and_trims_it(string input, string expected)
    {
        // Act
        var code = ClassificationCode.Create(input);

        // Assert
        Assert.Equal(expected, code.Value);
    }

    [Fact]
    public void Two_codes_written_differently_are_the_same_value()
    {
        // The reason normalisation happens at construction: a seed file and a
        // provider feed will not agree on case, and the taxonomy has to.
        Assert.Equal(ClassificationCode.Create("tech-soft"), ClassificationCode.Create("TECH-SOFT"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("T")]
    [InlineData("TECH SOFT")]
    [InlineData("TECH_SOFT")]
    [InlineData("TECH.SOFT")]
    public void An_unusable_code_is_rejected(string? input)
    {
        Assert.False(ClassificationCode.TryCreate(input, out _));
        Assert.Throws<DomainValidationException>(() => ClassificationCode.Create(input));
    }

    [Theory]
    [InlineData("-TECH")]
    [InlineData("TECH-")]
    public void A_dangling_hyphen_is_rejected(string input)
    {
        // Otherwise "TECH-" and "TECH" read as the same node and compare
        // unequal, which is the worst of both.
        Assert.False(ClassificationCode.TryCreate(input, out _));
    }

    [Fact]
    public void A_code_longer_than_the_limit_is_rejected()
    {
        var tooLong = new string('A', ClassificationCode.MaxLength + 1);

        Assert.False(ClassificationCode.TryCreate(tooLong, out _));
    }

    [Fact]
    public void A_code_at_the_length_limit_is_accepted()
    {
        var atLimit = new string('A', ClassificationCode.MaxLength);

        Assert.True(ClassificationCode.TryCreate(atLimit, out var code));
        Assert.Equal(atLimit, code.Value);
    }
}
