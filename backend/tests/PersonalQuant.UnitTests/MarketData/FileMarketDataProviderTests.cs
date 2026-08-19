using System.Text;
using PersonalQuant.Application.MarketData;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;
using PersonalQuant.Infrastructure.MarketData;

namespace PersonalQuant.UnitTests.MarketData;

/// <summary>
/// Verifies the file-backed reference provider.
/// </summary>
/// <remarks>
/// It is the implementation that keeps <see cref="IMarketDataProvider"/> from
/// being an interface shaped around one vendor's API, so its contract — parse,
/// serve the requested range, fail loudly on a malformed export — is worth
/// proving.
/// </remarks>
public sealed class FileMarketDataProviderTests : IDisposable
{
    private const string Header = "timestamp,open,high,low,close,volume,turnover";

    private static readonly DateTimeOffset From = new(2026, 8, 24, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 8, 29, 0, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "pqt-market-data-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task An_export_is_parsed_into_bars()
    {
        WriteExport(
            "FPT",
            "2026-08-24,100,110,95,105,1000,105000",
            "2026-08-25,105,115,100,112,1200,134400");

        // Act
        var result = await CreateProvider().FetchBarsAsync(
            Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Bars.Count);
        Assert.Equal("text/csv", result.ContentType);
        Assert.Equal(From, result.Bars[0].OpenedAtUtc);
        Assert.Equal(100m, result.Bars[0].Open);
        Assert.Equal(105000m, result.Bars[0].Turnover);
    }

    [Fact]
    public async Task Only_the_requested_range_is_served()
    {
        // Range filtering here is what a range request means. Rows outside it
        // are not rejections; nothing asked for them.
        WriteExport(
            "FPT",
            "2026-08-20,100,110,95,105,1000,",
            "2026-08-24,100,110,95,105,1000,",
            "2026-09-05,100,110,95,105,1000,");

        // Act
        var result = await CreateProvider().FetchBarsAsync(
            Request(), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(From, Assert.Single(result.Bars).OpenedAtUtc);
    }

    [Fact]
    public async Task The_retained_payload_is_the_slice_that_was_served()
    {
        // Keeping the whole export on every nightly run would copy the entire
        // history into the raw table each time.
        WriteExport(
            "FPT",
            "2026-08-20,100,110,95,105,1000,",
            "2026-08-24,100,110,95,105,1000,");

        var result = await CreateProvider().FetchBarsAsync(
            Request(), TestContext.Current.CancellationToken);

        Assert.Contains("2026-08-24", result.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("2026-08-20", result.Payload, StringComparison.Ordinal);
        Assert.StartsWith(Header, result.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Columns_are_read_by_name_rather_than_by_position()
    {
        // A silently reordered export is exactly the failure validation exists
        // to catch, and it must not be able to originate here.
        WriteExportWithHeader(
            "FPT",
            "close,timestamp,volume,open,high,low",
            "105,2026-08-24,1000,100,110,95");

        var result = await CreateProvider().FetchBarsAsync(
            Request(), TestContext.Current.CancellationToken);

        var bar = Assert.Single(result.Bars);
        Assert.Equal(100m, bar.Open);
        Assert.Equal(105m, bar.Close);
        Assert.Null(bar.Turnover);
    }

    [Fact]
    public async Task A_missing_export_is_a_stated_failure_rather_than_an_empty_success()
    {
        // A silent zero-bar success would look identical to a market holiday.
        var exception = await Assert.ThrowsAsync<MarketDataProviderException>(
            () => CreateProvider().FetchBarsAsync(
                Request(), TestContext.Current.CancellationToken));

        Assert.False(exception.IsTransient);
        Assert.Contains("FPT", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_export_missing_a_required_column_is_refused()
    {
        WriteExportWithHeader("FPT", "timestamp,open,high,low", "2026-08-24,100,110,95");

        var exception = await Assert.ThrowsAsync<MarketDataProviderException>(
            () => CreateProvider().FetchBarsAsync(
                Request(), TestContext.Current.CancellationToken));

        Assert.Contains("close", exception.Message, StringComparison.Ordinal);
        Assert.Contains("volume", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unreadable_number_names_the_line_and_the_column()
    {
        WriteExport("FPT", "2026-08-24,100,110,95,not-a-price,1000,");

        var exception = await Assert.ThrowsAsync<MarketDataProviderException>(
            () => CreateProvider().FetchBarsAsync(
                Request(), TestContext.Current.CancellationToken));

        Assert.Contains("Line 2", exception.Message, StringComparison.Ordinal);
        Assert.Contains("close", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_date_only_timestamp_is_read_as_midnight_utc()
    {
        // The daily-bar convention: a Vietnamese session lies wholly inside
        // one UTC day, so the trading date and the UTC date agree.
        WriteExport("FPT", "2026-08-24,100,110,95,105,1000,");

        var result = await CreateProvider().FetchBarsAsync(
            Request(), TestContext.Current.CancellationToken);

        Assert.Equal(From, Assert.Single(result.Bars).OpenedAtUtc);
        Assert.Equal(TimeSpan.Zero, result.Bars[0].OpenedAtUtc.Offset);
    }

    [Fact]
    public async Task Blank_lines_are_ignored()
    {
        WriteExport("FPT", "2026-08-24,100,110,95,105,1000,", string.Empty, "   ");

        var result = await CreateProvider().FetchBarsAsync(
            Request(), TestContext.Current.CancellationToken);

        Assert.Single(result.Bars);
    }

    [Fact]
    public async Task An_export_holding_only_a_header_serves_nothing_without_failing()
    {
        // An instrument that has not traded in the requested window is a
        // legitimate empty answer.
        WriteExport("FPT");

        var result = await CreateProvider().FetchBarsAsync(
            Request(), TestContext.Current.CancellationToken);

        Assert.Empty(result.Bars);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private FileMarketDataProvider CreateProvider() => new(_root);

    private void WriteExport(string ticker, params string[] rows) =>
        WriteExportWithHeader(ticker, Header, rows);

    // Distinctly named rather than overloaded. Two params overloads differing
    // only by a leading string are both applicable to the same call, and the
    // compiler picks the second — silently turning the first data row into the
    // header.
    private void WriteExportWithHeader(string ticker, string headerLine, params string[] rows)
    {
        var directory = Path.Combine(_root, "1d");
        Directory.CreateDirectory(directory);

        var content = new StringBuilder(headerLine).Append('\n');

        foreach (var row in rows)
        {
            content.Append(row).Append('\n');
        }

        File.WriteAllText(Path.Combine(directory, $"{ticker}.csv"), content.ToString(), Encoding.UTF8);
    }

    private static MarketDataRequest Request()
    {
        Assert.True(MarketDataRequest.TryCreate(
            InstrumentId.New(),
            Ticker.Create("FPT"),
            ExchangeCode.Create("HOSE"),
            BarInterval.OneDay,
            From,
            To,
            out var request,
            out var problem),
            problem);

        return request;
    }
}
