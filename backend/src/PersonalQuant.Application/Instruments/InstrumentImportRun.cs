using PersonalQuant.Application.Exchanges;
using PersonalQuant.Domain.Exchanges;
using PersonalQuant.Domain.Instruments;
using PersonalQuant.Domain.MarketData;

namespace PersonalQuant.Application.Instruments;

/// <summary>
/// The state one import run accumulates as it walks a provider's rows.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the run commits once at the end, which means the database
/// cannot answer questions about what the run has already decided. An
/// instrument created for row 12 is invisible to a repository query on row 400,
/// and without this the same security would be created twice — once for
/// <c>FPT</c> and once for <c>FPT.HM</c> — with the second insert failing on
/// the unique index and taking the whole import with it.
/// </para>
/// <para>
/// It doubles as a lookup cache. A symbol list repeats the same exchange
/// thousands of times, and resolving it thousands of times would make the
/// import's cost quadratic in nothing useful.
/// </para>
/// </remarks>
/// <param name="source">The provider being imported.</param>
/// <param name="occurredAtUtc">The instant stamped on everything the run creates.</param>
internal sealed class ImportRun(SourceCode source, DateTimeOffset occurredAtUtc)
{
    private readonly Dictionary<string, ExchangeId?> _exchanges = new(StringComparer.Ordinal);
    private readonly Dictionary<AliasKey, InstrumentId> _aliasOwners = [];
    private readonly HashSet<AliasKey> _absentAliases = [];
    private readonly Dictionary<(ExchangeId Exchange, string Ticker), InstrumentId> _created = [];
    private readonly HashSet<string> _symbolsSeen = new(StringComparer.Ordinal);
    private readonly List<InstrumentImportRejection> _rejections = [];

    private int _created_count;
    private int _matched;
    private int _enriched;
    private int _aliases;

    /// <summary>Gets the provider being imported.</summary>
    public SourceCode Source { get; } = source;

    /// <summary>Gets the instant stamped on everything the run creates.</summary>
    public DateTimeOffset OccurredAtUtc { get; } = occurredAtUtc;

    /// <summary>
    /// Reports whether a raw symbol has not already been handled by this run.
    /// </summary>
    /// <param name="rawSymbol">The provider's spelling.</param>
    /// <returns><see langword="true"/> the first time the symbol is seen.</returns>
    public bool FirstSightOf(string rawSymbol) => _symbolsSeen.Add(rawSymbol);

    /// <summary>Records a refused row.</summary>
    /// <param name="row">The row as the source reported it.</param>
    /// <param name="reason">Why it was refused.</param>
    /// <param name="detail">A short, specific explanation.</param>
    public void Reject(
        ProviderInstrument row,
        InstrumentImportRejectionReason reason,
        string detail) =>
        _rejections.Add(new InstrumentImportRejection(row, reason, detail));

    /// <summary>Records that a row resolved to an instrument and changed nothing.</summary>
    public void Matched() => _matched++;

    /// <summary>Records that a row gave an existing instrument a fact it lacked.</summary>
    public void Enriched() => _enriched++;

    /// <summary>Records that a row created a new instrument.</summary>
    /// <param name="exchangeId">The venue it was created on.</param>
    /// <param name="ticker">The ticker it took.</param>
    /// <param name="instrumentId">The identifier issued to it.</param>
    public void Created(ExchangeId exchangeId, Ticker ticker, InstrumentId instrumentId)
    {
        _created_count++;
        _created[(exchangeId, ticker.Value)] = instrumentId;
    }

    /// <summary>Records how many aliases a row wrote.</summary>
    /// <param name="count">The number written.</param>
    public void AliasesRecorded(int count) => _aliases += count;

    /// <summary>
    /// Resolves a venue code, remembering the answer including a negative one.
    /// </summary>
    /// <param name="code">The venue code.</param>
    /// <param name="exchanges">The venue repository.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The venue, or <see langword="null"/> when it is not held.</returns>
    public async Task<ExchangeId?> ResolveExchangeAsync(
        ExchangeCode code,
        IExchangeRepository exchanges,
        CancellationToken cancellationToken)
    {
        if (_exchanges.TryGetValue(code.Value, out var cached))
        {
            return cached;
        }

        var exchange = await exchanges
            .FindByCodeAsync(code, cancellationToken)
            .ConfigureAwait(false);

        var resolved = exchange?.Id;
        _exchanges[code.Value] = resolved;

        return resolved;
    }

    /// <summary>
    /// Finds the instrument an alias names, consulting what this run has
    /// already staged before going to the database.
    /// </summary>
    /// <param name="value">The scheme and value.</param>
    /// <param name="source">The provider, for a provider-scoped scheme.</param>
    /// <param name="instruments">The instrument master.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The instrument, or <see langword="null"/> when the alias is unknown.</returns>
    public async Task<InstrumentId?> FindByIdentifierAsync(
        IdentifierValue value,
        SourceCode? source,
        IInstrumentRepository instruments,
        CancellationToken cancellationToken)
    {
        var key = AliasKey.For(value, source);

        if (_aliasOwners.TryGetValue(key, out var owner))
        {
            return owner;
        }

        if (_absentAliases.Contains(key))
        {
            return null;
        }

        var identifier = await instruments
            .FindIdentifierAsync(value, source, cancellationToken)
            .ConfigureAwait(false);

        if (identifier is null)
        {
            _absentAliases.Add(key);
            return null;
        }

        _aliasOwners[key] = identifier.InstrumentId;
        return identifier.InstrumentId;
    }

    /// <summary>
    /// Finds the instrument currently holding a ticker, including one this run
    /// has just created.
    /// </summary>
    /// <param name="exchangeId">The venue.</param>
    /// <param name="ticker">The ticker.</param>
    /// <param name="instruments">The instrument master.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The instrument, or <see langword="null"/> when the ticker is free.</returns>
    public async Task<InstrumentId?> FindActiveByTickerAsync(
        ExchangeId exchangeId,
        Ticker ticker,
        IInstrumentRepository instruments,
        CancellationToken cancellationToken)
    {
        if (_created.TryGetValue((exchangeId, ticker.Value), out var staged))
        {
            return staged;
        }

        var instrument = await instruments
            .FindActiveByTickerAsync(exchangeId, ticker, cancellationToken)
            .ConfigureAwait(false);

        return instrument?.Id;
    }

    /// <summary>
    /// Claims an alias for an instrument, unless something already holds it.
    /// </summary>
    /// <remarks>
    /// The guard against writing a row the unique index would reject. An alias
    /// already in the database, or already staged by this run, is not written
    /// again — and an alias held by a <em>different</em> instrument is not
    /// stolen, because that situation was already reported as a conflict.
    /// </remarks>
    /// <param name="value">The scheme and value.</param>
    /// <param name="source">The provider, for a provider-scoped scheme.</param>
    /// <param name="instrumentId">The instrument to attach it to.</param>
    /// <returns><see langword="true"/> when the alias should be written.</returns>
    public bool TryClaim(IdentifierValue value, SourceCode? source, InstrumentId instrumentId)
    {
        var key = AliasKey.For(value, source);

        if (_aliasOwners.ContainsKey(key))
        {
            return false;
        }

        _aliasOwners[key] = instrumentId;
        _absentAliases.Remove(key);

        return true;
    }

    /// <summary>Closes the run and reports what it did.</summary>
    /// <param name="rowsRead">How many rows the source returned.</param>
    /// <returns>The report.</returns>
    public InstrumentImportReport ToReport(int rowsRead) =>
        new(Source.Value, rowsRead, _created_count, _matched, _enriched, _aliases, _rejections);

    /// <summary>
    /// An alias identity, in the shape the unique indexes enforce.
    /// </summary>
    /// <remarks>
    /// Source is part of the key because two providers legitimately use the
    /// same decorated symbol for different securities. For a global scheme it
    /// is empty, which is what makes such an alias unique across the whole
    /// master rather than per provider.
    /// </remarks>
    private readonly record struct AliasKey(IdentifierScheme Scheme, string Value, string Source)
    {
        public static AliasKey For(IdentifierValue value, SourceCode? source) =>
            new(value.Scheme, value.Value, source?.Value ?? string.Empty);
    }
}
