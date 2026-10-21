using PersonalQuant.Infrastructure.CorporateActions;

namespace PersonalQuant.UnitTests.CorporateActions;

/// <summary>
/// Reads the committed coverage fixture through the file source (ADR-032).
/// </summary>
public sealed class FileCorporateActionCoverageProviderTests
{
    [Fact]
    public async Task The_committed_fixture_records_the_eight_names_swept_in_sprint_8()
    {
        var rows = await new FileCorporateActionCoverageProvider(FixturePath())
            .ListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["BCM.HM", "MSN.HM", "MWG.HM", "PLX.HM", "POW.HM", "VNM.HM", "VPL.HM", "VRE.HM"],
            rows.Select(row => row.Symbol));
        Assert.All(rows, row => Assert.Equal(new DateOnly(2026, 10, 21), row.SweptOn));
        Assert.All(rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Source)));
        Assert.Equal(new DateOnly(2025, 5, 13), rows.Single(row => row.Symbol == "VPL.HM").CompleteFrom);
        Assert.All(rows.Where(row => row.Symbol != "VPL.HM"), row => Assert.Equal(new DateOnly(2023, 7, 1), row.CompleteFrom));
    }

    [Fact]
    public async Task A_source_note_may_hold_commas_because_it_is_the_last_column()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coverage-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(
            path,
            "ticker,complete_from,swept_on,source\nVNM.HM,2023-07-01,2026-10-21,VSDC, then the issuer's site\nVNM.HM,01/07/2023,2026-10-21,VSDC\n",
            TestContext.Current.CancellationToken);

        try
        {
            var rows = await new FileCorporateActionCoverageProvider(path).ListAsync(TestContext.Current.CancellationToken);

            Assert.Equal("VSDC, then the issuer's site", rows[0].Source);
            Assert.Null(rows[1].CompleteFrom);
            Assert.Equal(3, rows[1].LineNumber);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string FixturePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "data", "fixtures", "corporate-action-coverage.csv");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("data/fixtures/corporate-action-coverage.csv was not found above the test binary.");
    }
}
