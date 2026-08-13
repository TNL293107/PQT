using PersonalQuant.Infrastructure.Configuration;

namespace PersonalQuant.UnitTests.Configuration;

public sealed class RedisOptionsTests
{
    [Fact]
    public void BuildConfiguration_registers_the_configured_endpoint()
    {
        // Arrange
        var options = new RedisOptions { Host = "redis", Port = 6380 };

        // Act
        var configuration = options.BuildConfiguration();

        // Assert
        var endpoint = Assert.Single(configuration.EndPoints);
        Assert.Contains("redis", endpoint.ToString(), StringComparison.Ordinal);
        Assert.Contains("6380", endpoint.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void BuildConfiguration_does_not_abort_when_redis_is_unavailable()
    {
        // Start-up must survive an unavailable Redis; readiness reports it
        // instead. This is the setting that makes that true.
        // Arrange
        var options = new RedisOptions { Host = "redis" };

        // Act
        var configuration = options.BuildConfiguration();

        // Assert
        Assert.False(configuration.AbortOnConnectFail);
    }

    [Fact]
    public void BuildConfiguration_omits_the_password_when_none_is_configured()
    {
        // Arrange
        var options = new RedisOptions { Host = "redis", Password = "   " };

        // Act
        var configuration = options.BuildConfiguration();

        // Assert
        Assert.Null(configuration.Password);
    }

    [Fact]
    public void BuildConfiguration_applies_the_password_when_one_is_configured()
    {
        // Arrange
        var options = new RedisOptions { Host = "redis", Password = "local-redis-password" };

        // Act
        var configuration = options.BuildConfiguration();

        // Assert
        Assert.Equal("local-redis-password", configuration.Password);
    }
}
