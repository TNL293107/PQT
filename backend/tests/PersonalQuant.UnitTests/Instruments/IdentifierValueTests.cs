using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.UnitTests.Instruments;

/// <summary>
/// Verifies what may be recorded as an identifier value.
/// </summary>
/// <remarks>
/// A check digit catches a typed or transposed character and nothing more. It
/// is still worth checking: an identifier with a corrupt character maps to no
/// instrument, silently and permanently, because nothing downstream revisits
/// it.
/// </remarks>
public sealed class IdentifierValueTests
{
    [Theory]
    // The ISO 6166 documented example, and a widely published equity ISIN.
    [InlineData("AU0000XVGZA3")]
    [InlineData("US0378331005")]
    public void A_well_formed_isin_is_accepted(string value)
    {
        Assert.True(IdentifierValue.TryCreate(IdentifierScheme.Isin, value, out var identifier, out _));
        Assert.Equal(value, identifier.Value);
        Assert.Equal(IdentifierScheme.Isin, identifier.Scheme);
    }

    [Fact]
    public void An_isin_is_upper_cased_and_trimmed()
    {
        Assert.True(IdentifierValue.TryCreate(
            IdentifierScheme.Isin, "  us0378331005  ", out var identifier, out _));

        Assert.Equal("US0378331005", identifier.Value);
    }

    [Theory]
    [InlineData("US0378331004")]  // check digit off by one
    [InlineData("US0378331050")]  // two characters transposed
    public void An_isin_with_a_bad_check_digit_is_rejected(string value) =>
        Assert.False(IdentifierValue.TryCreate(IdentifierScheme.Isin, value, out _, out _));

    [Theory]
    [InlineData("US037833100")]    // too short
    [InlineData("US03783310055")]  // too long
    [InlineData("1S0378331005")]   // country prefix is not two letters
    [InlineData("US03783310X5")]   // check position is not a digit
    [InlineData("US0378-331005")]  // punctuation
    [InlineData(null)]
    [InlineData("")]
    public void A_malformed_isin_is_rejected(string? value)
    {
        Assert.False(IdentifierValue.TryCreate(IdentifierScheme.Isin, value, out _, out _));
        Assert.Throws<DomainValidationException>(
            () => IdentifierValue.Create(IdentifierScheme.Isin, value));
    }

    [Theory]
    // A published FIGI, and the documented specification example.
    [InlineData("BBG000BLNNH6")]
    [InlineData("NRG92C84SB39")]
    public void A_well_formed_figi_is_accepted(string value)
    {
        Assert.True(IdentifierValue.TryCreate(IdentifierScheme.Figi, value, out var identifier, out _));
        Assert.Equal(value, identifier.Value);
    }

    [Theory]
    [InlineData("BBG000BLNNH5")]  // check digit off by one
    [InlineData("BBX000BLNNH6")]  // third character is not G
    [InlineData("BBG000BLENH6")]  // contains a vowel
    [InlineData("BBG000BLNNH")]   // too short
    public void A_malformed_figi_is_rejected(string value) =>
        Assert.False(IdentifierValue.TryCreate(IdentifierScheme.Figi, value, out _, out _));

    [Fact]
    public void A_figi_and_an_isin_are_not_interchangeable()
    {
        // The same twelve characters cannot say which scheme was intended,
        // which is why the value and its scheme travel together.
        Assert.True(IdentifierValue.TryCreate(IdentifierScheme.Figi, "BBG000BLNNH6", out _, out _));
        Assert.False(IdentifierValue.TryCreate(IdentifierScheme.Isin, "BBG000BLNNH6", out _, out _));
    }

    [Theory]
    [InlineData("FPT", "FPT")]
    [InlineData("fpt.hm", "FPT.HM")]
    [InlineData("FPT:VN", "FPT:VN")]
    [InlineData("HOSE:FPT", "HOSE:FPT")]
    [InlineData("VN30F2312", "VN30F2312")]
    [InlineData("  fpt-hnx  ", "FPT-HNX")]
    public void A_provider_symbol_keeps_its_decoration(string input, string expected)
    {
        // The stored alias has to be the provider's exact spelling, or a
        // lookup by what the provider sent will miss it.
        Assert.True(IdentifierValue.TryCreate(
            IdentifierScheme.ProviderSymbol, input, out var identifier, out _));

        Assert.Equal(expected, identifier.Value);
    }

    [Theory]
    [InlineData(".FPT")]        // leading punctuation
    [InlineData("FPT.")]        // trailing punctuation
    [InlineData("FPT HM")]      // a space is two fields concatenated by mistake
    [InlineData("FPT@HM")]
    [InlineData(null)]
    [InlineData("")]
    public void A_malformed_provider_symbol_is_rejected(string? value) =>
        Assert.False(IdentifierValue.TryCreate(IdentifierScheme.ProviderSymbol, value, out _, out _));

    [Fact]
    public void A_provider_symbol_over_the_length_limit_is_rejected()
    {
        var tooLong = new string('A', IdentifierValue.MaxProviderSymbolLength + 1);

        Assert.False(IdentifierValue.TryCreate(IdentifierScheme.ProviderSymbol, tooLong, out _, out _));
    }

    [Fact]
    public void An_undeclared_scheme_is_rejected() =>
        Assert.False(IdentifierValue.TryCreate(
            IdentifierScheme.Unspecified, "US0378331005", out _, out _));

    [Fact]
    public void Only_isin_and_figi_name_a_security_everywhere()
    {
        Assert.True(IdentifierScheme.Isin.IsGlobal());
        Assert.True(IdentifierScheme.Figi.IsGlobal());
        Assert.False(IdentifierScheme.ProviderSymbol.IsGlobal());
    }

    [Fact]
    public void Two_values_of_the_same_scheme_compare_by_value() =>
        Assert.Equal(
            IdentifierValue.Create(IdentifierScheme.Isin, "us0378331005"),
            IdentifierValue.Create(IdentifierScheme.Isin, "US0378331005"));
}
