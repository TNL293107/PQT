using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.UnitTests.Instruments;

/// <summary>
/// Covers the folding rule both sides of a search depend on. If a query and a
/// stored name fold differently, the security is simply unfindable, and
/// nothing further up the stack can detect that.
/// </summary>
public sealed class InstrumentSearchTextTests
{
    [Theory]
    [InlineData("fpt", "FPT")]
    [InlineData("Fpt", "FPT")]
    [InlineData("  fpt  ", "FPT")]
    public void Folding_upper_cases_and_trims(string input, string expected)
    {
        // Act
        var folded = InstrumentSearchText.Normalise(input);

        // Assert
        Assert.Equal(expected, folded);
    }

    [Theory]
    [InlineData("Công ty Cổ phần FPT", "CONG TY CO PHAN FPT")]
    [InlineData("Ngân hàng Thương mại", "NGAN HANG THUONG MAI")]
    [InlineData("Đầu tư", "DAU TU")]
    public void Vietnamese_diacritics_are_folded_away(string input, string expected)
    {
        // Nobody types accents into a terminal, so a name carrying them has to
        // be reachable without them.
        // Act
        var folded = InstrumentSearchText.Normalise(input);

        // Assert
        Assert.Equal(expected, folded);
    }

    [Fact]
    public void The_letter_D_with_stroke_is_folded_explicitly()
    {
        // Đ is a distinct Vietnamese letter rather than D plus a combining
        // mark, so Unicode decomposition alone leaves it untouched.
        // Act
        var folded = InstrumentSearchText.Normalise("đầu");

        // Assert
        Assert.Equal("DAU", folded);
    }

    [Fact]
    public void Internal_whitespace_collapses_to_single_spaces()
    {
        // Act
        var folded = InstrumentSearchText.Normalise("  Hoa   Phat\tGroup\n");

        // Assert
        Assert.Equal("HOA PHAT GROUP", folded);
    }

    [Fact]
    public void Punctuation_is_preserved()
    {
        // Removing it would quietly change which names count as an exact
        // match, and no query depends on it being gone.
        // Act
        var folded = InstrumentSearchText.Normalise("Saigon - Hanoi Securities J.S.C.");

        // Assert
        Assert.Equal("SAIGON - HANOI SECURITIES J.S.C.", folded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_input_folds_to_an_empty_string(string? input)
    {
        // Act
        var folded = InstrumentSearchText.Normalise(input);

        // Assert
        Assert.Equal(string.Empty, folded);
    }

    [Fact]
    public void Folding_is_idempotent()
    {
        // Folded text is stored in the database and folded queries are matched
        // against it. If a second pass changed the value, a name written back
        // through the domain would stop matching itself.
        // Arrange
        const string Original = "Ngân hàng   Ngoại thương";

        // Act
        var once = InstrumentSearchText.Normalise(Original);
        var twice = InstrumentSearchText.Normalise(once);

        // Assert
        Assert.Equal(once, twice);
    }

    [Fact]
    public void Folding_never_lengthens_the_text()
    {
        // The folded value is stored in a column sized from the original, so
        // decomposition must not be able to overflow it.
        // Arrange
        const string Accented = "Cổ phần Đầu tư Xây dựng và Phát triển";

        // Act
        var folded = InstrumentSearchText.Normalise(Accented);

        // Assert
        Assert.True(folded.Length <= Accented.Length);
    }
}
