using PersonalQuant.Application.MarketData;
using PersonalQuant.Infrastructure.Fundamentals;

namespace PersonalQuant.UnitTests.Fundamentals;

/// <summary>
/// Verifies that the IDS collector's documents are read as written, cell text
/// unparsed, and that a document of another shape is refused by name.
/// </summary>
public sealed class IdsFilingFileProviderTests
{
    // Invented values in the collector's shape; nothing here is a real filing.
    private const string Document = """
        {
          "schema": "pqt.ids-filing/1",
          "source": "IDS",
          "reference": "TST-20220422-0a1b2c3d4e5f",
          "ticker": "TST",
          "venue": "HOSE",
          "reportName": "Báo cáo tài chính Hợp nhất - Quý",
          "summary": "Báo cáo tài chính hợp nhất quý 1 năm 2022",
          "submittedOn": "2022-04-22",
          "submittedAtUtc": null,
          "fiscalYear": 2022,
          "fiscalQuarter": 1,
          "document": "test.pdf",
          "fetchedAtUtc": "2026-10-13T09:00:00Z",
          "tables": [
            {
              "name": "BCDKT",
              "columns": ["Số cuối kỳ", "Số đầu kỳ"],
              "rows": [ { "label": "TỔNG CỘNG TÀI SẢN", "code": "270", "values": ["5,000", ""] } ]
            }
          ]
        }
        """;

    [Fact]
    public void A_collector_document_is_read_as_written()
    {
        var filing = IdsFilingFileProvider.Parse(Document, "test.json");

        Assert.Equal(("TST-20220422-0a1b2c3d4e5f", "TST", new DateOnly(2022, 4, 22), 1), (filing.Reference, filing.Ticker, filing.SubmittedOn, filing.FiscalQuarter));
        Assert.Null(filing.SubmittedAtUtc);
        var row = Assert.Single(Assert.Single(filing.Tables).Rows);
        Assert.Equal(["5,000", ""], row.Values);
        Assert.Equal(Document, filing.RawPayload);
    }

    [Fact]
    public void The_date_the_search_files_a_filing_under_is_read_and_the_later_date_dates_it()
    {
        var filing = IdsFilingFileProvider.Parse(
            Document.Replace("\"submittedAtUtc\": null,", "\"submittedAtUtc\": null, \"indexedOn\": \"2022-04-27\",", StringComparison.Ordinal),
            "indexed.json");

        Assert.Equal((new DateOnly(2022, 4, 27), new DateOnly(2022, 4, 27)), (filing.IndexedOn!.Value, filing.DisclosedOn));
        Assert.Equal(new DateOnly(2022, 4, 22), IdsFilingFileProvider.Parse(Document, "plain.json").DisclosedOn);
    }

    [Fact]
    public void A_document_of_another_schema_is_refused_by_name()
    {
        var error = Assert.Throws<MarketDataProviderException>(
            () => IdsFilingFileProvider.Parse(Document.Replace("pqt.ids-filing/1", "other/1", StringComparison.Ordinal), "odd.json"));

        Assert.Contains("odd.json", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_missing_its_date_is_refused() =>
        Assert.Throws<MarketDataProviderException>(() => IdsFilingFileProvider.Parse(
            Document.Replace("\"submittedOn\": \"2022-04-22\",", string.Empty, StringComparison.Ordinal), "dateless.json"));
}
