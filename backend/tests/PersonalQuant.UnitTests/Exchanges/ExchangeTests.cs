using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.Exchanges;

namespace PersonalQuant.UnitTests.Exchanges;

public sealed class ExchangeTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    private const string TimeZone = "Asia/Ho_Chi_Minh";

    [Fact]
    public void Register_issues_an_identifier_and_stamps_creation()
    {
        // Act
        var exchange = Register();

        // Assert
        Assert.False(exchange.Id.IsEmpty);
        Assert.Equal(Now, exchange.CreatedAtUtc);
        Assert.Equal(Now, exchange.UpdatedAtUtc);
    }

    [Fact]
    public void Register_trims_the_name()
    {
        // Act
        var exchange = Exchange.Register(
            ExchangeCode.Create("HOSE"), "  A Venue  ", TimeZone, Now);

        // Assert
        Assert.Equal("A Venue", exchange.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_rejects_an_absent_name(string name)
    {
        // Act + Assert
        Assert.Throws<DomainValidationException>(() =>
            Exchange.Register(ExchangeCode.Create("HOSE"), name, TimeZone, Now));
    }

    [Fact]
    public void Register_rejects_a_time_zone_the_platform_does_not_know()
    {
        // An unusable zone stored now would only surface much later, when a
        // trading-day boundary is computed.
        // Act + Assert
        Assert.Throws<DomainValidationException>(() =>
            Exchange.Register(ExchangeCode.Create("HOSE"), "A Venue", "Mars/Olympus", Now));
    }

    [Fact]
    public void Register_leaves_the_mic_unset_when_none_is_supplied()
    {
        // MIC coverage for Vietnamese venues varies by provider, so it is
        // optional and never identity.
        // Act
        var exchange = Register();

        // Assert
        Assert.Null(exchange.Mic);
    }

    [Fact]
    public void Register_normalises_a_supplied_mic()
    {
        // Act
        var exchange = Exchange.Register(
            ExchangeCode.Create("HOSE"), "A Venue", TimeZone, Now, mic: " xstc ");

        // Assert
        Assert.Equal("XSTC", exchange.Mic);
    }

    [Theory]
    [InlineData("XST")]
    [InlineData("XSTCX")]
    [InlineData("XS-C")]
    public void Register_rejects_a_malformed_mic(string mic)
    {
        // Act + Assert
        Assert.Throws<DomainValidationException>(() =>
            Exchange.Register(ExchangeCode.Create("HOSE"), "A Venue", TimeZone, Now, mic));
    }

    [Fact]
    public void Rename_advances_the_updated_stamp_but_not_creation()
    {
        // Arrange
        var exchange = Register();
        var later = Now.AddDays(1);

        // Act
        exchange.Rename("Renamed Venue", later);

        // Assert
        Assert.Equal("Renamed Venue", exchange.Name);
        Assert.Equal(Now, exchange.CreatedAtUtc);
        Assert.Equal(later, exchange.UpdatedAtUtc);
    }

    [Fact]
    public void An_audit_stamp_must_be_utc()
    {
        // A local-time stamp looks authoritative and silently shifts when the
        // process moves between machines.
        // Arrange
        var local = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.FromHours(7));

        // Act + Assert
        Assert.Throws<DomainValidationException>(() =>
            Exchange.Register(ExchangeCode.Create("HOSE"), "A Venue", TimeZone, local));
    }

    [Fact]
    public void An_update_cannot_predate_creation()
    {
        // Arrange
        var exchange = Register();

        // Act + Assert
        Assert.Throws<DomainValidationException>(() =>
            exchange.Rename("Renamed Venue", Now.AddDays(-1)));
    }

    private static Exchange Register() =>
        Exchange.Register(ExchangeCode.Create("HOSE"), "A Venue", TimeZone, Now);
}
