using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Exchanges;

namespace PersonalQuant.UnitTests.Exchanges;

public sealed class ExchangeCodeTests
{
    [Theory]
    [InlineData("HOSE", "HOSE")]
    [InlineData("hose", "HOSE")]
    [InlineData(" hnx ", "HNX")]
    [InlineData("UPCOM", "UPCOM")]
    public void Create_normalises_case_and_whitespace(string input, string expected)
    {
        // Act
        var code = ExchangeCode.Create(input);

        // Assert
        Assert.Equal(expected, code.Value);
    }

    [Theory]
    [InlineData("H")]
    [InlineData("HO-SE")]
    [InlineData("HO SE")]
    [InlineData("")]
    [InlineData(null)]
    public void Create_rejects_a_malformed_code(string? input)
    {
        // Act + Assert
        Assert.Throws<DomainValidationException>(() => ExchangeCode.Create(input));
    }

    [Fact]
    public void Create_rejects_a_value_beyond_the_maximum_length()
    {
        // Arrange
        var tooLong = new string('A', ExchangeCode.MaxLength + 1);

        // Act + Assert
        Assert.Throws<DomainValidationException>(() => ExchangeCode.Create(tooLong));
    }

    [Fact]
    public void Codes_compare_by_value()
    {
        // Act
        var first = ExchangeCode.Create("hose");
        var second = ExchangeCode.Create("HOSE");

        // Assert
        Assert.Equal(first, second);
    }
}
