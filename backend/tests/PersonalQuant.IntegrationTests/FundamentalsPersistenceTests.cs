using Microsoft.Extensions.DependencyInjection;
using PersonalQuant.Application.Abstractions;
using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Fundamentals;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Fundamentals;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.IntegrationTests;

/// <summary>
/// Verifies fundamentals against real PostgreSQL: that a report and its lines
/// read back as recorded, that a restatement keeps the superseded reading,
/// and that the schema refuses a filing recorded twice or two current
/// readings of one line.
/// </summary>
/// <param name="containers">Shared container fixture.</param>
[Collection(DependencyContainerTests.Name)]
public sealed class FundamentalsPersistenceTests(DependencyContainerFixture containers)
{
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 13, 3, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Vietnam = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    private static readonly SourceCode Ids = SourceCode.Create("IDS");
    private static readonly FiscalQuarter Q1 = FiscalQuarter.Create(2022, 1);

    [Fact]
    public async Task A_report_and_its_lines_read_back_as_recorded()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrument = await ArrangeAsync(scope, "FRA");
        var reference = $"ids-{Guid.NewGuid():N}";
        var report = Report(instrument, reference);
        scope.Fundamentals.Add(report);
        scope.Fundamentals.Add(Line(report, FinancialStatement.BalanceSheet, "270", Q1, PeriodSpan.Instant, 36_617_350_544_580m));
        scope.Fundamentals.Add(Line(report, FinancialStatement.IncomeStatement, "70", Q1, PeriodSpan.Quarter, 1_234.5678m));
        scope.Fundamentals.Add(Line(report, FinancialStatement.IncomeStatement, "10", Q1.YearEarlier, PeriodSpan.Quarter, 7_586_327_669_954m));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var reader = await CreateScopeAsync();
        var found = await reader.Fundamentals.FindReportAsync(Ids, reference, TestContext.Current.CancellationToken);
        var lines = await reader.Fundamentals.ListLinesAsync([report.Id], TestContext.Current.CancellationToken);

        Assert.NotNull(found);
        Assert.Equal((Q1, report.KnownFromUtc, FinancialTemplate.NonFinancialCircular200), (found.Period, found.KnownFromUtc, found.Template));
        Assert.Equal(3, lines.Count);
        Assert.Equal(1_234.5678m, lines.Single(line => line.LineCode == "70").Value);
        Assert.Equal(Q1.YearEarlier, lines.Single(line => line.LineCode == "10").Period);
    }

    [Fact]
    public async Task A_restated_reading_keeps_the_superseded_one()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrument = await ArrangeAsync(scope, "FRB");
        var report = Report(instrument, $"ids-{Guid.NewGuid():N}");
        scope.Fundamentals.Add(report);
        scope.Fundamentals.Add(Line(report, FinancialStatement.IncomeStatement, "10", Q1, PeriodSpan.Quarter, 100m));
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using (var restater = await CreateScopeAsync())
        {
            var current = Assert.Single(await restater.Fundamentals.ListCurrentLinesForUpdateAsync(
                report.Id, TestContext.Current.CancellationToken));
            restater.Fundamentals.Add(current.Restate(101m, ValueBasis.Vendor, SourceCode.Create("VCI"), RecordedAt.AddDays(1)));
            await restater.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reader = await CreateScopeAsync();
        var lines = await reader.Fundamentals.ListLinesAsync([report.Id], TestContext.Current.CancellationToken);

        Assert.Equal(
            [(0, 100m, false), (1, 101m, true)],
            lines.OrderBy(line => line.Revision).Select(line => (line.Revision, line.Value, line.IsCurrent)));
    }

    [Fact]
    public async Task A_filing_cannot_be_recorded_twice()
    {
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrument = await ArrangeAsync(scope, "FRC");
        var reference = $"ids-{Guid.NewGuid():N}";
        scope.Fundamentals.Add(Report(instrument, reference));
        scope.Fundamentals.Add(Report(instrument, reference));

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Contains("ux_financial_reports_source_reference", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_line_of_a_report_cannot_have_two_current_readings()
    {
        // Two first readings of the same line: the reading index sees the same
        // revision twice, and that is the mistake an importer that forgot to
        // look for the current reading would make.
        Assert.SkipWhen(containers.UnavailableReason is not null, containers.UnavailableReason ?? string.Empty);

        await using var scope = await CreateScopeAsync();
        var instrument = await ArrangeAsync(scope, "FRD");
        var report = Report(instrument, $"ids-{Guid.NewGuid():N}");
        scope.Fundamentals.Add(report);
        scope.Fundamentals.Add(Line(report, FinancialStatement.IncomeStatement, "10", Q1, PeriodSpan.Quarter, 100m));
        scope.Fundamentals.Add(Line(report, FinancialStatement.IncomeStatement, "10", Q1, PeriodSpan.Quarter, 101m));

        var error = await Assert.ThrowsAnyAsync<Exception>(
            () => scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken));

        Assert.Contains("ux_statement_lines_", error.ToString(), StringComparison.Ordinal);
    }

    private static FinancialReport Report(InstrumentId instrument, string reference) =>
        FinancialReport.Record(
            instrument,
            Ids,
            reference,
            FinancialReportKind.Quarterly,
            FinancialStatementScope.Consolidated,
            FinancialTemplate.NonFinancialCircular200,
            Q1,
            "Báo cáo tài chính hợp nhất quý 1 năm 2022",
            new DateOnly(2022, 4, 22),
            disclosedAtUtc: null,
            Vietnam,
            isCorrection: false,
            valuesFiled: true,
            RecordedAt);

    private static StatementLine Line(
        FinancialReport report,
        FinancialStatement statement,
        string code,
        FiscalQuarter period,
        PeriodSpan span,
        decimal value) =>
        StatementLine.Observe(report, statement, code, period, span, value, ValueBasis.AsFiled, Ids, RecordedAt);

    private static async Task<InstrumentId> ArrangeAsync(FundamentalsScope scope, string tag)
    {
        var suffix = Guid.NewGuid().ToString("N")[..5].ToUpperInvariant();
        var venue = Exchange.Register(ExchangeCode.Create($"F{suffix}"), "Venue", "Asia/Ho_Chi_Minh", RecordedAt);
        scope.Exchanges.Add(venue);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        var instrument = Instrument.Register(
            venue.Id, Ticker.Create(tag), $"{tag} Test Company", AssetType.Equity, CurrencyCode.Vnd, RecordedAt);
        instrument.List(RecordedAt);
        scope.Instruments.Add(instrument);
        await scope.UnitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        return instrument.Id;
    }

    private async Task<FundamentalsScope> CreateScopeAsync()
    {
        var factory = PersonalQuantApiFactory.WithDependencies(
            containers.Postgres,
            containers.Redis,
            applyMigrations: true);

        using var client = factory.CreateClient();
        _ = await client.GetAsync(new Uri("/health", UriKind.Relative), TestContext.Current.CancellationToken);

        return new FundamentalsScope(factory);
    }

    private sealed class FundamentalsScope : IAsyncDisposable
    {
        private readonly PersonalQuantApiFactory _factory;
        private readonly AsyncServiceScope _scope;

        public FundamentalsScope(PersonalQuantApiFactory factory)
        {
            _factory = factory;
            _scope = factory.Services.CreateAsyncScope();

            UnitOfWork = _scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Exchanges = _scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
            Instruments = _scope.ServiceProvider.GetRequiredService<IInstrumentRepository>();
            Fundamentals = _scope.ServiceProvider.GetRequiredService<IFundamentalsRepository>();
        }

        public IUnitOfWork UnitOfWork { get; }

        public IExchangeRepository Exchanges { get; }

        public IInstrumentRepository Instruments { get; }

        public IFundamentalsRepository Fundamentals { get; }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _factory.DisposeAsync();
        }
    }
}
