using PersonalQuant.Domain.Common;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Infrastructure.Configuration;

namespace PersonalQuant.UnitTests.Configuration;

/// <summary>
/// Verifies that the market data settings turn into a usable policy and
/// schedule.
/// </summary>
/// <remarks>
/// These are numbers in a file, and a setting that is silently wrong produces a
/// retry policy that never waits or a schedule that does nothing once per hour.
/// Validation happens at composition so a bad value fails a deployment rather
/// than a job at 2am.
/// </remarks>
public sealed class MarketDataOptionsTests
{
    [Fact]
    public void The_defaults_build_a_valid_policy()
    {
        var policy = new MarketDataOptions().BuildPolicy();

        Assert.Equal(3, policy.MaxAttempts);
        Assert.Equal(TimeSpan.FromMilliseconds(500), policy.InitialBackoff);
        Assert.Equal(TimeSpan.FromSeconds(30), policy.ProviderTimeout);
        Assert.Equal(TimeSpan.FromDays(365), policy.InitialBackfill);
    }

    [Fact]
    public void The_defaults_leave_every_source_and_schedule_off()
    {
        // Starting the API should not begin reading an external source.
        var options = new MarketDataOptions();

        Assert.Equal(string.Empty, options.FileProviderDirectory);
        Assert.Equal(string.Empty, options.InstrumentListPath);
        Assert.Equal(string.Empty, options.TradingCalendarPath);
        Assert.False(options.ImportReferenceDataOnStartup);
        Assert.False(options.IngestOnSchedule);
    }

    [Fact]
    public void The_scheduled_resolution_defaults_to_daily()
    {
        // One resolution per deployment. Ingesting all six would multiply
        // provider calls by six for data nothing reads yet.
        Assert.Equal(BarInterval.OneDay, new MarketDataOptions().BuildIngestionInterval());
    }

    [Theory]
    [InlineData(1, BarInterval.OneMinute)]
    [InlineData(60, BarInterval.OneHour)]
    [InlineData(1440, BarInterval.OneDay)]
    public void A_declared_resolution_is_accepted(int minutes, BarInterval expected)
    {
        var options = new MarketDataOptions { IngestionBarIntervalMinutes = minutes };

        Assert.Equal(expected, options.BuildIngestionInterval());
    }

    [Theory]
    [InlineData(7)]
    [InlineData(120)]
    [InlineData(1439)]
    public void A_resolution_the_system_does_not_record_is_refused(int minutes)
    {
        // Otherwise the value reaches the pipeline and is skipped once per
        // instrument per pass, forever, with nothing saying why.
        var options = new MarketDataOptions { IngestionBarIntervalMinutes = minutes };

        Assert.Throws<DomainValidationException>(() => options.BuildIngestionInterval());
    }

    [Fact]
    public void An_unusable_retry_policy_is_refused()
    {
        var options = new MarketDataOptions
        {
            InitialBackoffMilliseconds = 60_000,
            MaxBackoffMilliseconds = 1_000,
        };

        Assert.Throws<DomainValidationException>(() => options.BuildPolicy());
    }
}
