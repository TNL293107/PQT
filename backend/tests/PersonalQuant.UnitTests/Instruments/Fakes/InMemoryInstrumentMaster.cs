using PersonalQuant.Application.Exchanges;
using PersonalQuant.Application.Instruments;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.UnitTests.Instruments.Fakes;

/// <summary>
/// An in-memory instrument master that behaves like the real one.
/// </summary>
/// <remarks>
/// <para>
/// Not a stub. The import pipeline's rules are all about what it finds when it
/// looks — a provider symbol already claimed, an ISIN pointing somewhere else,
/// a ticker taken on a venue — so a repository that answered "not supported"
/// would leave nothing to test.
/// </para>
/// <para>
/// Writes are staged and only become visible on
/// <see cref="Commit"/>, which is what the real unit of work does. Without
/// that, the run's own in-memory index — the thing that stops one security
/// being created twice from two spellings — would never be exercised.
/// </para>
/// </remarks>
internal sealed class InMemoryInstrumentMaster : IInstrumentRepository
{
    private readonly Dictionary<InstrumentId, Instrument> _instruments = [];
    private readonly List<InstrumentIdentifier> _identifiers = [];
    private readonly List<Instrument> _stagedInstruments = [];
    private readonly List<InstrumentIdentifier> _stagedIdentifiers = [];

    /// <summary>Gets every committed instrument.</summary>
    public IReadOnlyCollection<Instrument> Instruments => _instruments.Values;

    /// <summary>Gets every committed alias.</summary>
    public IReadOnlyList<InstrumentIdentifier> Identifiers => _identifiers;

    /// <summary>Makes the staged writes visible, as a commit would.</summary>
    public void Commit()
    {
        foreach (var instrument in _stagedInstruments)
        {
            _instruments[instrument.Id] = instrument;
        }

        _identifiers.AddRange(_stagedIdentifiers);
        _stagedInstruments.Clear();
        _stagedIdentifiers.Clear();
    }

    /// <summary>Adds an instrument as if it were already committed.</summary>
    /// <param name="instrument">The instrument to seed.</param>
    public void Seed(Instrument instrument) => _instruments[instrument.Id] = instrument;

    /// <summary>Adds an alias as if it were already committed.</summary>
    /// <param name="identifier">The alias to seed.</param>
    public void Seed(InstrumentIdentifier identifier) => _identifiers.Add(identifier);

    public Task<Instrument?> FindByIdAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_instruments.GetValueOrDefault(id));

    public Task<Instrument?> FindActiveByTickerAsync(
        ExchangeId exchangeId,
        Ticker ticker,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_instruments.Values.FirstOrDefault(instrument =>
            instrument.ExchangeId == exchangeId
            && instrument.Ticker == ticker
            && instrument.Status != InstrumentStatus.Delisted));

    public Task<IReadOnlyList<Instrument>> ListTickerHistoryAsync(
        ExchangeId exchangeId,
        Ticker ticker,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Instrument>>(
            [.. _instruments.Values.Where(instrument =>
                instrument.ExchangeId == exchangeId && instrument.Ticker == ticker)]);

    public Task<bool> IsTickerTakenAsync(
        ExchangeId exchangeId,
        Ticker ticker,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_instruments.Values.Any(instrument =>
            instrument.ExchangeId == exchangeId
            && instrument.Ticker == ticker
            && instrument.Status != InstrumentStatus.Delisted));

    public Task<InstrumentIdentifier?> FindIdentifierAsync(
        IdentifierValue value,
        SourceCode? source,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_identifiers.FirstOrDefault(identifier =>
            identifier.Matches(value, source)));

    public Task<IReadOnlyList<InstrumentIdentifier>> ListIdentifiersAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InstrumentIdentifier>>(
            [.. _identifiers.Where(identifier => identifier.InstrumentId == id)]);

    public void Add(Instrument instrument) => _stagedInstruments.Add(instrument);

    public void AddIdentifier(InstrumentIdentifier identifier) =>
        _stagedIdentifiers.Add(identifier);

    public Task<IReadOnlyList<InstrumentSearchResult>> SearchAsync(
        InstrumentSearchCriteria criteria,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Search is covered by the integration tests.");

    public Task<IReadOnlyList<InstrumentSearchResult>> ListActiveByTickerAsync(
        Ticker ticker,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by import.");

    public Task<InstrumentSearchResult?> FindResultByIdAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by import.");

    public Task<InstrumentDetail?> FindDetailByIdAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Not exercised by import.");

    public Task<InstrumentPage> ListAsync(
        InstrumentListCriteria criteria,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Paging is covered by the integration tests.");

    public Task<IReadOnlyList<RelatedInstrument>> ListRelatedAsync(
        InstrumentId id,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Relations are covered by the integration tests.");
}

/// <summary>An in-memory venue repository.</summary>
internal sealed class InMemoryExchanges : IExchangeRepository
{
    private readonly List<Exchange> _exchanges = [];

    /// <summary>Registers a venue and returns its identifier.</summary>
    /// <param name="code">The operating code.</param>
    /// <param name="occurredAtUtc">The audit instant.</param>
    /// <returns>The new venue's identifier.</returns>
    public ExchangeId Add(string code, DateTimeOffset occurredAtUtc)
    {
        var exchange = Exchange.Register(
            ExchangeCode.Create(code), $"{code} Venue", "Asia/Ho_Chi_Minh", occurredAtUtc);

        _exchanges.Add(exchange);
        return exchange.Id;
    }

    public Task<Exchange?> FindByIdAsync(
        ExchangeId id,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_exchanges.Find(exchange => exchange.Id == id));

    public Task<Exchange?> FindByCodeAsync(
        ExchangeCode code,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_exchanges.Find(exchange => exchange.Code == code));

    public Task<IReadOnlyList<Exchange>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<Exchange>>([.. _exchanges]);

    public void Add(Exchange exchange) => _exchanges.Add(exchange);
}

/// <summary>A source returning whatever a test hands it.</summary>
internal sealed class ScriptedInstrumentProvider(
    SourceCode code,
    IReadOnlyList<ProviderInstrument> rows) : IInstrumentProvider
{
    public SourceCode Code { get; } = code;

    public Task<IReadOnlyList<ProviderInstrument>> ListInstrumentsAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(rows);
}
