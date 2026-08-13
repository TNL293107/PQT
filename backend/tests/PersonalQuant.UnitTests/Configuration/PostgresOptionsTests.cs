using Npgsql;
using PersonalQuant.Infrastructure.Configuration;

namespace PersonalQuant.UnitTests.Configuration;

public sealed class PostgresOptionsTests
{
    [Fact]
    public void BuildConnectionString_maps_every_configured_value()
    {
        // Arrange
        var options = new PostgresOptions
        {
            Host = "postgres",
            Port = 6543,
            Database = "personal_quant",
            Username = "quant_user",
            Password = "local-development-password",
            CommandTimeoutSeconds = 42,
            ConnectTimeoutSeconds = 7,
        };

        // Act
        var builder = new NpgsqlConnectionStringBuilder(options.BuildConnectionString());

        // Assert
        Assert.Equal("postgres", builder.Host);
        Assert.Equal(6543, builder.Port);
        Assert.Equal("personal_quant", builder.Database);
        Assert.Equal("quant_user", builder.Username);
        Assert.Equal("local-development-password", builder.Password);
        Assert.Equal(42, builder.CommandTimeout);
        Assert.Equal(7, builder.Timeout);
    }

    [Fact]
    public void BuildConnectionString_escapes_values_instead_of_concatenating_them()
    {
        // A password containing the connection string delimiters must not be
        // able to inject additional keywords.
        // Arrange
        var options = new PostgresOptions
        {
            Host = "postgres",
            Database = "personal_quant",
            Username = "quant_user",
            Password = "p;a=ss'word\"x",
        };

        // Act
        var builder = new NpgsqlConnectionStringBuilder(options.BuildConnectionString());

        // Assert
        Assert.Equal("p;a=ss'word\"x", builder.Password);
        Assert.Equal("personal_quant", builder.Database);
    }

    [Fact]
    public void BuildConnectionString_never_includes_server_error_detail()
    {
        // IncludeErrorDetail would put row values into exception messages,
        // which would then reach the logs.
        // Arrange
        var options = new PostgresOptions
        {
            Host = "postgres",
            Database = "personal_quant",
            Username = "quant_user",
            Password = "secret",
        };

        // Act
        var builder = new NpgsqlConnectionStringBuilder(options.BuildConnectionString());

        // Assert
        Assert.False(builder.IncludeErrorDetail);
    }
}
