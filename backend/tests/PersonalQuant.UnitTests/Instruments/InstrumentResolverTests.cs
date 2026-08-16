using Microsoft.Extensions.Logging.Abstractions;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Currencies;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;

namespace PersonalQuant.UnitTests.Instruments;

/// <summary>
/// Covers symbol resolution — the path every command, watchlist and alert will
/// take to turn "FPT" into a canonical identifier without going near a UI.
/// </summary>
public sealed class InstrumentResolverTests
{
    private static readonly ExchangeCode Hose = ExchangeCode.Create("HOSE");
    private static readonly ExchangeCode Upcom = ExchangeCode.Create("UPCOM");

    [Fact]
    public async Task A_known_symbol_resolves_to_its_canonical_instrument()
    {
        // Arrange
        var fpt = Result("FPT", "FPT Corporation", Hose);
        var resolver = ResolverOver(fpt);

        // Act
        var resolution = await resolver.ResolveAsync("FPT", null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(InstrumentResolutionOutcome.Resolved, resolution.Outcome);
        Assert.Equal(fpt.InstrumentId, resolution.Instrument?.InstrumentId);
    }

    [Theory]
    [InlineData("fpt")]
    [InlineData("  FPT  ")]
    [InlineData("Fpt")]
    public async Task Resolution_normalises_the_symbol(string symbol)
    {
        // A command bar hands over whatever was typed. Casing and stray
        // whitespace are not a different security.
        // Arrange
        var resolver = ResolverOver(Result("FPT", "FPT Corporation", Hose));

        // Act
        var resolution = await resolver.ResolveAsync(symbol, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(InstrumentResolutionOutcome.Resolved, resolution.Outcome);
        Assert.Equal("FPT", resolution.Query);
    }

    [Fact]
    public async Task An_unknown_symbol_reports_not_found()
    {
        // Arrange
        var resolver = ResolverOver();

        // Act
        var resolution = await resolver.ResolveAsync("ZZZZ", null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(InstrumentResolutionOutcome.NotFound, resolution.Outcome);
        Assert.Null(resolution.Instrument);
        Assert.Empty(resolution.Candidates);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("FPT.HM")]
    [InlineData("a-symbol-far-longer-than-any-ticker-could-ever-be")]
    public async Task Text_that_cannot_be_a_ticker_reports_not_found(string? symbol)
    {
        // Not an error: the caller asked whether a security answers to this,
        // and the answer is no. A provider-decorated symbol such as FPT.HM is
        // in the same position until the alias workstream lands.
        // Arrange
        var resolver = ResolverOver(Result("FPT", "FPT Corporation", Hose));

        // Act
        var resolution = await resolver.ResolveAsync(symbol, null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(InstrumentResolutionOutcome.NotFound, resolution.Outcome);
    }

    [Fact]
    public async Task A_symbol_live_on_two_venues_is_reported_as_ambiguous()
    {
        // Ticker uniqueness is enforced per exchange, so this is possible.
        // Picking the first row would eventually attach one company's prices
        // to another company's identifier.
        // Arrange
        var onHose = Result("AAA", "A Listed Company", Hose);
        var onUpcom = Result("AAA", "Another Listed Company", Upcom);
        var resolver = ResolverOver(onHose, onUpcom);

        // Act
        var resolution = await resolver.ResolveAsync("AAA", null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(InstrumentResolutionOutcome.Ambiguous, resolution.Outcome);
        Assert.Null(resolution.Instrument);
        Assert.Equal(2, resolution.Candidates.Count);
    }

    [Fact]
    public async Task An_exchange_disambiguates_a_shared_symbol()
    {
        // Arrange
        var onHose = Result("AAA", "A Listed Company", Hose);
        var onUpcom = Result("AAA", "Another Listed Company", Upcom);
        var resolver = ResolverOver(onHose, onUpcom);

        // Act
        var resolution = await resolver.ResolveAsync("AAA", Upcom, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(InstrumentResolutionOutcome.Resolved, resolution.Outcome);
        Assert.Equal(onUpcom.InstrumentId, resolution.Instrument?.InstrumentId);
    }

    [Fact]
    public async Task An_exchange_that_lists_nothing_under_the_symbol_reports_not_found()
    {
        // Arrange
        var resolver = ResolverOver(Result("FPT", "FPT Corporation", Hose));

        // Act
        var resolution = await resolver.ResolveAsync("FPT", Upcom, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(InstrumentResolutionOutcome.NotFound, resolution.Outcome);
    }

    [Fact]
    public async Task An_unassigned_identifier_never_reaches_the_repository()
    {
        // A client can send anything. An empty GUID is not a lookup worth
        // issuing, and it must not be able to match a row.
        // Arrange
        var repository = new FakeInstrumentRepository(Result("FPT", "FPT Corporation", Hose));
        var resolver = new InstrumentResolver(repository, NullLogger<InstrumentResolver>.Instance);

        // Act
        var found = await resolver.FindByIdAsync(default, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(found);
        Assert.Equal(0, repository.FindByIdCallCount);
    }

    [Fact]
    public async Task A_known_identifier_reads_back_the_instrument()
    {
        // Arrange
        var fpt = Result("FPT", "FPT Corporation", Hose);
        var resolver = ResolverOver(fpt);

        // Act
        var found = await resolver.FindByIdAsync(fpt.InstrumentId, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(fpt.InstrumentId, found?.InstrumentId);
    }

    private static InstrumentResolver ResolverOver(params InstrumentSearchResult[] instruments) =>
        new(new FakeInstrumentRepository(instruments), NullLogger<InstrumentResolver>.Instance);

    private static InstrumentSearchResult Result(string ticker, string name, ExchangeCode exchange) =>
        new(
            InstrumentId.New(),
            Ticker.Create(ticker),
            name,
            AssetType.Equity,
            exchange,
            CurrencyCode.Vnd,
            InstrumentStatus.Listed,
            MatchKind: null);

    /// <summary>
    /// An in-memory instrument master.
    /// </summary>
    /// <remarks>
    /// Resolution's own rules — normalising the symbol, applying the exchange
    /// filter, and choosing between resolved, not found and ambiguous — are
    /// what these tests are about, and none of them need a database. The SQL
    /// behind <see cref="IInstrumentRepository"/> is covered by the
    /// integration tests, against real PostgreSQL.
    /// </remarks>
    private sealed class FakeInstrumentRepository(params InstrumentSearchResult[] instruments)
        : IInstrumentRepository
    {
        public int FindByIdCallCount { get; private set; }

        public Task<IReadOnlyList<InstrumentSearchResult>> ListActiveByTickerAsync(
            Ticker ticker,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<InstrumentSearchResult>>(
                [.. instruments
                    .Where(instrument => instrument.Ticker == ticker)
                    .OrderBy(instrument => instrument.ExchangeCode.Value, StringComparer.Ordinal)]);

        public Task<InstrumentSearchResult?> FindResultByIdAsync(
            InstrumentId id,
            CancellationToken cancellationToken = default)
        {
            FindByIdCallCount++;

            return Task.FromResult(
                instruments.FirstOrDefault(instrument => instrument.InstrumentId == id));
        }

        public Task<IReadOnlyList<InstrumentSearchResult>> SearchAsync(
            InstrumentSearchCriteria criteria,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Search is covered by the integration tests.");

        public Task<Instrument?> FindByIdAsync(
            InstrumentId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by resolution.");

        public Task<Instrument?> FindActiveByTickerAsync(
            ExchangeId exchangeId,
            Ticker ticker,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by resolution.");

        public Task<IReadOnlyList<Instrument>> ListTickerHistoryAsync(
            ExchangeId exchangeId,
            Ticker ticker,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by resolution.");

        public Task<bool> IsTickerTakenAsync(
            ExchangeId exchangeId,
            Ticker ticker,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by resolution.");

        public Task<InstrumentDetail?> FindDetailByIdAsync(
            InstrumentId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by resolution.");

        public void Add(Instrument instrument) =>
            throw new NotSupportedException("Not exercised by resolution.");
    }
}
