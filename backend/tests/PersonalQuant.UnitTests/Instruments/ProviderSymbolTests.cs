using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.UnitTests.Instruments;

/// <summary>
/// Verifies how a provider's decorated symbol is split into a ticker and a
/// venue hint.
/// </summary>
/// <remarks>
/// The same security arrives as <c>FPT</c>, <c>FPT.HM</c>, <c>FPT:VN</c> and
/// <c>HOSE:FPT</c>, and every one of those has to reach the same canonical
/// instrument.
/// </remarks>
public sealed class ProviderSymbolTests
{
    [Theory]
    [InlineData("FPT")]
    [InlineData("fpt")]
    [InlineData("  FPT  ")]
    public void A_bare_symbol_parses_to_a_ticker_with_no_hint(string value)
    {
        Assert.True(ProviderSymbol.TryParse(value, out var symbol, out var problem), problem);

        Assert.Equal("FPT", symbol.Ticker.Value);
        Assert.Null(symbol.VenueHint);
    }

    [Theory]
    [InlineData("FPT.HM", "HOSE")]
    [InlineData("FPT.HOSE", "HOSE")]
    [InlineData("HOSE:FPT", "HOSE")]
    [InlineData("FPT-HNX", "HNX")]
    [InlineData("FPT/HN", "HNX")]
    [InlineData("BSR.UP", "UPCOM")]
    [InlineData("BSR_UPCOM", "UPCOM")]
    public void A_decorated_symbol_yields_a_ticker_and_a_venue(string value, string expectedVenue)
    {
        Assert.True(ProviderSymbol.TryParse(value, out var symbol, out var problem), problem);

        Assert.Equal(expectedVenue, symbol.VenueHint!.Value);
    }

    [Fact]
    public void The_ticker_survives_whichever_side_the_venue_is_on()
    {
        Assert.True(ProviderSymbol.TryParse("FPT.HM", out var suffixed, out _));
        Assert.True(ProviderSymbol.TryParse("HOSE:FPT", out var prefixed, out _));

        Assert.Equal(suffixed.Ticker, prefixed.Ticker);
    }

    [Fact]
    public void A_country_suffix_is_not_read_as_a_venue()
    {
        // VN names the country, not an exchange. Guessing HOSE from it would
        // attach HNX and UPCOM securities to the wrong venue.
        Assert.True(ProviderSymbol.TryParse("FPT:VN", out var symbol, out var problem), problem);

        Assert.Equal("FPT", symbol.Ticker.Value);
        Assert.Null(symbol.VenueHint);
    }

    [Fact]
    public void A_venue_decoration_that_is_also_a_valid_ticker_is_read_as_the_venue()
    {
        // HN, HM and UP are all well-formed tickers. Testing for a ticker
        // first would consume the venue and leave the security nameless.
        Assert.True(ProviderSymbol.TryParse("SHS.HN", out var symbol, out var problem), problem);

        Assert.Equal("SHS", symbol.Ticker.Value);
        Assert.Equal("HNX", symbol.VenueHint!.Value);
    }

    [Fact]
    public void The_original_spelling_is_preserved()
    {
        // It is stored as an alias, so the next import from the same provider
        // is a lookup rather than a re-parse.
        Assert.True(ProviderSymbol.TryParse("fpt.hm", out var symbol, out _));

        Assert.Equal("FPT.HM", symbol.Raw);
    }

    [Fact]
    public void A_symbol_with_two_possible_tickers_is_refused()
    {
        // One of them is the security and the other is a venue this system has
        // never heard of. Getting it backwards attaches a price series to the
        // wrong company.
        Assert.False(ProviderSymbol.TryParse("ABC.DEF", out _, out var problem));
        Assert.Contains("more than one", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_symbol_naming_only_venues_is_refused()
    {
        Assert.False(ProviderSymbol.TryParse("HOSE.HN", out _, out var problem));
        Assert.Contains("names no security", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("FPT@HM")]
    public void An_unusable_symbol_is_refused(string? value) =>
        Assert.False(ProviderSymbol.TryParse(value, out _, out _));

    [Theory]
    [InlineData("VNM")]
    [InlineData("VNM.HM")]
    [InlineData("VNM:VN")]
    public void A_ticker_that_looks_like_a_country_code_is_still_a_ticker(string value)
    {
        // VNM is Vinamilk. Listing it as a market qualifier alongside VN would
        // make its own symbol unparseable, which is why only VN is there.
        Assert.True(ProviderSymbol.TryParse(value, out var symbol, out var problem), problem);

        Assert.Equal("VNM", symbol.Ticker.Value);
    }

    [Fact]
    public void A_derivative_symbol_survives_parsing()
    {
        // Longer than an equity ticker and carrying digits, which is exactly
        // what the ticker rules were widened for.
        Assert.True(ProviderSymbol.TryParse("VN30F2312.HM", out var symbol, out var problem), problem);

        Assert.Equal("VN30F2312", symbol.Ticker.Value);
        Assert.Equal("HOSE", symbol.VenueHint!.Value);
    }
}
