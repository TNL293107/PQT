using PersonalQuant.Api.Configuration;

namespace PersonalQuant.UnitTests.Configuration;

public sealed class CorsOptionsTests
{
    [Fact]
    public void ParseAllowedOrigins_splits_and_trims_the_configured_list()
    {
        // Arrange
        var options = new CorsOptions
        {
            AllowedOrigins = "http://localhost:3000, http://localhost:5173",
        };

        // Act
        var origins = options.ParseAllowedOrigins();

        // Assert
        Assert.Equal(["http://localhost:3000", "http://localhost:5173"], origins);
    }

    [Fact]
    public void ParseAllowedOrigins_returns_empty_when_nothing_is_configured()
    {
        // An unset value must mean "no browser origin allowed", never
        // "every origin allowed".
        // Arrange
        var options = new CorsOptions();

        // Act
        var origins = options.ParseAllowedOrigins();

        // Assert
        Assert.Empty(origins);
    }

    [Fact]
    public void ParseAllowedOrigins_ignores_empty_entries()
    {
        // Arrange
        var options = new CorsOptions { AllowedOrigins = "http://localhost:3000,,  ," };

        // Act
        var origins = options.ParseAllowedOrigins();

        // Assert
        Assert.Equal(["http://localhost:3000"], origins);
    }
}
