using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.UnitTests.Instruments;

public sealed class TickerTests
{
    [Theory]
    [InlineData("FPT", "FPT")]
    [InlineData("fpt", "FPT")]
    [InlineData("  fpt  ", "FPT")]
    [InlineData("VN30F2312", "VN30F2312")]
    [InlineData("FUEVFVND", "FUEVFVND")]
    public void Create_normalises_case_and_whitespace(string input, string expected)
    {
        // Act
        var ticker = Ticker.Create(input);

        // Assert
        Assert.Equal(expected, ticker.Value);
    }

    [Theory]
    [InlineData("FPT.HM")]
    [InlineData("FPT:VN")]
    [InlineData("FPT-VN")]
    [InlineData("FPT VN")]
    public void Create_rejects_provider_decorated_symbols(string input)
    {
        // Provider symbology is not canonical. Accepting a decorated spelling
        // here would let one security enter the master twice.
        // Act + Assert
        Assert.Throws<DomainValidationException>(() => Ticker.Create(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_an_absent_value(string? input)
    {
        // Act + Assert
        Assert.Throws<DomainValidationException>(() => Ticker.Create(input));
    }

    [Fact]
    public void Create_rejects_a_value_beyond_the_maximum_length()
    {
        // Arrange
        var tooLong = new string('A', Ticker.MaxLength + 1);

        // Act + Assert
        Assert.Throws<DomainValidationException>(() => Ticker.Create(tooLong));
    }

    [Fact]
    public void Create_accepts_a_value_at_the_maximum_length()
    {
        // Arrange
        var atLimit = new string('A', Ticker.MaxLength);

        // Act
        var ticker = Ticker.Create(atLimit);

        // Assert
        Assert.Equal(atLimit, ticker.Value);
    }

    [Fact]
    public void TryCreate_reports_failure_without_throwing()
    {
        // Act
        var created = Ticker.TryCreate("FPT.HM", out var ticker);

        // Assert
        Assert.False(created);
        Assert.Null(ticker);
    }

    [Fact]
    public void Tickers_compare_by_value()
    {
        // Two spellings of one symbol must be the same ticker, or a lookup
        // would depend on how the caller typed it.
        // Act
        var first = Ticker.Create("fpt");
        var second = Ticker.Create("FPT");

        // Assert
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }
}
