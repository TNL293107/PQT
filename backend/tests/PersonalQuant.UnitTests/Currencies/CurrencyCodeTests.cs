using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Currencies;

namespace PersonalQuant.UnitTests.Currencies;

public sealed class CurrencyCodeTests
{
    [Theory]
    [InlineData("VND", "VND")]
    [InlineData("vnd", "VND")]
    [InlineData(" usd ", "USD")]
    public void Create_normalises_case_and_whitespace(string input, string expected)
    {
        // Act
        var currency = CurrencyCode.Create(input);

        // Assert
        Assert.Equal(expected, currency.Value);
    }

    [Theory]
    [InlineData("VN")]
    [InlineData("VNDX")]
    [InlineData("V1D")]
    [InlineData("")]
    [InlineData(null)]
    public void Create_rejects_a_value_that_is_not_three_letters(string? input)
    {
        // Act + Assert
        Assert.Throws<DomainValidationException>(() => CurrencyCode.Create(input));
    }

    [Fact]
    public void Vnd_is_available_as_a_named_value()
    {
        // Every HOSE, HNX and UPCOM listing quotes in dong, so the code is
        // referenced often enough to warrant not re-parsing it.
        // Assert
        Assert.Equal("VND", CurrencyCode.Vnd.Value);
        Assert.Equal(CurrencyCode.Create("VND"), CurrencyCode.Vnd);
    }
}
